#!/usr/bin/env bash
# Runs the BR-RES-002 / BR-RES-004 database-mechanism probe against a throwaway local PostgreSQL.
# Needs PostgreSQL server binaries (initdb, pg_ctl, psql). Prints PASS/FAIL per check.
# Usage: db-probe/run-probe.sh            (run as a normal user, or as root with a 'postgres' account)
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
PGBIN="${PGBIN:-$(dirname "$(command -v pg_ctl 2>/dev/null || ls /usr/lib/postgresql/*/bin/pg_ctl | tail -1)")}"
WORK="$(mktemp -d)"
PORT="${PROBE_PORT:-55432}"
AS=""
if [ "$(id -u)" = "0" ]; then AS="runuser -u postgres --"; chown postgres "$WORK"; fi

pg() { $AS "$PGBIN/psql" -h "$WORK" -p "$PORT" -d probe -v ON_ERROR_STOP=0 -qAt "$@"; }
pass=0; fail=0
check() { if [ "$2" = "$3" ]; then echo "PASS  $1"; pass=$((pass+1)); else echo "FAIL  $1 (expected '$3', got '$2')"; fail=$((fail+1)); fi; }

$AS "$PGBIN/initdb" -D "$WORK/data" -A trust -U postgres >/dev/null
$AS "$PGBIN/pg_ctl" -D "$WORK/data" -o "-p $PORT -k $WORK -c listen_addresses=''" -l "$WORK/log" -w start >/dev/null
trap '$AS "$PGBIN/pg_ctl" -D "$WORK/data" -m fast stop >/dev/null; rm -rf "$WORK"' EXIT
$AS "$PGBIN/psql" -h "$WORK" -p "$PORT" -d postgres -U postgres -qc "CREATE DATABASE probe" >/dev/null
pg -U postgres -f "$HERE/schema.sql" >/dev/null

echo "Run at:      $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "PostgreSQL:  $(pg -U postgres -c 'SHOW server_version')"
echo "Commit:      $(git -C "$HERE/.." rev-parse --short HEAD 2>/dev/null || echo 'not a git checkout')"
echo

ITEM=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa
S1=11111111-1111-1111-1111-111111111111
S2=22222222-2222-2222-2222-222222222222
ins() { echo "INSERT INTO \"Reservations\" VALUES (gen_random_uuid(), '$1', '$2', '$3', now(), 1);"; }

# P1 — sequential second active reservation is refused by the index (BR-RES-002)
pg -U postgres -c "$(ins $ITEM $S1 Reserved)" >/dev/null
out=$(pg -U postgres -c "$(ins $ITEM $S2 CheckedOut)" 2>&1)
check "P1 second active reservation refused by IX_Reservations_OneActivePerItem" \
  "$(echo "$out" | grep -c 'IX_Reservations_OneActivePerItem')" "1"

# P2 — finished reservations never block the item (BR-RES-005)
pg -U postgres -c "UPDATE \"Reservations\" SET \"Status\"='Returned' WHERE \"EquipmentItemId\"='$ITEM'" >/dev/null
pg -U postgres -c "$(ins $ITEM $S2 Cancelled) $(ins $ITEM $S2 Returned) $(ins $ITEM $S2 Reserved)" >/dev/null 2>&1
check "P2 Cancelled/Returned rows coexist with one new active reservation" \
  "$(pg -U postgres -c "SELECT count(*) FROM \"Reservations\" WHERE \"EquipmentItemId\"='$ITEM' AND \"Status\" IN ('Reserved','CheckedOut')")" "1"

# P3 — TRUE concurrency: session A holds an uncommitted insert; session B inserts the same item at the same time.
ITEM2=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb
( pg -U postgres -c "BEGIN; $(ins $ITEM2 $S1 Reserved) SELECT pg_sleep(2); COMMIT;" >"$WORK/a.out" 2>&1 ) &
sleep 0.7
startB=$(date +%s.%N)
pg -U postgres -c "$(ins $ITEM2 $S2 Reserved)" >"$WORK/b.out" 2>&1
endB=$(date +%s.%N)
wait
waited=$(echo "$endB - $startB" | bc)
check "P3a session B blocked until A committed (waited ${waited}s, expect > 1)" "$(echo "$waited > 1" | bc)" "1"
check "P3b session B (the loser) got a unique violation" "$(grep -c 'IX_Reservations_OneActivePerItem' "$WORK/b.out")" "1"
check "P3c exactly one active reservation exists for the raced item" \
  "$(pg -U postgres -c "SELECT count(*) FROM \"Reservations\" WHERE \"EquipmentItemId\"='$ITEM2'")" "1"
check "P3d the winner was session A (student1)" \
  "$(pg -U postgres -c "SELECT \"StudentId\" FROM \"Reservations\" WHERE \"EquipmentItemId\"='$ITEM2'")" "$S1"

# P4 — atomic history (BR-RES-004): history insert fails inside the transaction -> reservation insert is undone too
pg -U postgres -c "DELETE FROM \"Reservations\" WHERE \"EquipmentItemId\"='$ITEM2'" >/dev/null
pg -U postgres -c "CREATE FUNCTION fail_history() RETURNS trigger AS \$\$ BEGIN RAISE EXCEPTION 'forced history failure'; END; \$\$ LANGUAGE plpgsql;
                   CREATE TRIGGER fail_history BEFORE INSERT ON \"HistoryEvents\" FOR EACH ROW EXECUTE FUNCTION fail_history();" >/dev/null
out=$(pg -U postgres -c "BEGIN;
  INSERT INTO \"Reservations\" VALUES ('cccccccc-cccc-cccc-cccc-cccccccccccc', '$ITEM2', '$S1', 'Reserved', now(), 1);
  INSERT INTO \"HistoryEvents\" VALUES (gen_random_uuid(), 'cccccccc-cccc-cccc-cccc-cccccccccccc', 'Created', 'Reserved', '$S1', now());
  COMMIT;" 2>&1)
check "P4a history insert was rejected" "$(echo "$out" | grep -c 'forced history failure')" "1"
check "P4b the reservation written earlier in the same transaction is gone" \
  "$(pg -U postgres -c "SELECT count(*) FROM \"Reservations\" WHERE \"Id\"='cccccccc-cccc-cccc-cccc-cccccccccccc'")" "0"
pg -U postgres -c "DROP TRIGGER fail_history ON \"HistoryEvents\"" >/dev/null

# P5 — optimistic concurrency (Version token): a stale UPDATE changes zero rows
pg -U postgres -c "INSERT INTO \"Reservations\" VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd', '$ITEM2', '$S1', 'Reserved', now(), 1)" >/dev/null
pg -U postgres -c "UPDATE \"Reservations\" SET \"Status\"='Cancelled', \"Version\"=2 WHERE \"Id\"='dddddddd-dddd-dddd-dddd-dddddddddddd' AND \"Version\"=1" >/dev/null
stale=$(pg -U postgres -c "WITH u AS (UPDATE \"Reservations\" SET \"Status\"='CheckedOut', \"Version\"=2 WHERE \"Id\"='dddddddd-dddd-dddd-dddd-dddddddddddd' AND \"Version\"=1 RETURNING 1) SELECT count(*) FROM u")
check "P5 stale pickup (expects Version=1, now 2) updates 0 rows" "$stale" "0"

echo
echo "Result: $pass passed, $fail failed"
[ "$fail" = "0" ]
