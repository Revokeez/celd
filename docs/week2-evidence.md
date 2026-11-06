# Week 2 evidence, trace and findings — Group 2 (Profile B)

**Prepared:** 2026-11-06 · **Baseline:** CELD SRS v1.1.1 · **Architecture:** ADR-001 (unchanged), ADR-002 (new)

> **Rule (Week 2 guide):** authored but unexecuted tests are not passes. This file keeps the two apart.
> Update the "Executed" table every time a check is actually run, with the commit SHA it ran against.

## 1. What has actually been executed

These ran in the AI workspace on 2026-11-06. The workspace had **no .NET SDK**, so no C# was compiled or run.

| Check | What it proves | Result | Evidence |
| --- | --- | --- | --- |
| DB mechanism probe, 9 checks, PostgreSQL 16.15 | The filtered unique index blocks a second active reservation, including a **true two-session race** (session B blocked about 1.3 s, then failed). Finished rows don't block. A failing history insert rolls back the reservation in the same transaction. A stale `Version` update changes 0 rows. | **9 / 9 pass** | `db-probe/probe-output-2026-11-06.txt` |
| Probe negative controls | The checks fail when the mechanism is removed: no index → 2 active reservations got through; two separate saves → orphaned reservation. So the passing run is meaningful. | **Behaved as expected** | same file |
| Client bundle (esbuild) | All TS/TSX parses; every import and export resolves. **Not** a type check. | Pass | run log only |
| Client render smoke, headless Chromium, **stub API** | Catalog table, text availability badges, empty state, not-found state, and Visitor sign-in prompt render with no runtime errors. | Pass | run log only |

**Scope warning:** the probe runs a hand-written mirror schema (`db-probe/schema.sql`), not the EF-generated one,
and it does not run the application. It shows the *mechanisms* work in PostgreSQL. It does **not** demonstrate AC-04 or AC-10.
Those need the integration tests below to run.

## 2. Authored but NOT yet run

| Item | Count | How to run | Status |
| --- | --- | --- | --- |
| `dotnet build` of server and tests | — | `dotnet build tests/Celd.Tests` | **Not run** |
| Service-unit tests (`Unit/`) | 18 test methods | `dotnet test tests/Celd.Tests --filter "FullyQualifiedName~Unit"` | **Not run** |
| Integration tests on PostgreSQL (`Integration/`) | 29 test methods | `dotnet test tests/Celd.Tests --filter "FullyQualifiedName~Integration"` | **Not run** |
| Client type check | — | `cd client && npm install && npm run typecheck` | **Not run** |
| Manual end-to-end procedure (NFR-TEST-002) | 1 procedure | `docs/acceptance-procedure.md` | **Not run** |
| Performance run (NFR-PERF-001) | 1 procedure | `docs/acceptance-procedure.md` part C | **Not run** |
| CI workflow | — | `.github/workflows/ci.yml`, on push | **Not run** |

**Expect the first build to surface some compile errors.** The code was written without a compiler. Fix them,
record what changed in the findings table (section 6), and only then record test results.

## 3. Executable test steps (Day 3)

```bash
# 0. One-time setup (README sections 1-3)
docker compose up -d

# 1. Build everything. Note the commit you are testing.
git rev-parse --short HEAD
dotnet build tests/Celd.Tests

# 2. Profile B emphasis: service-unit tests (no Docker needed)
dotnet test tests/Celd.Tests --filter "FullyQualifiedName~Unit" \
  --logger "trx;LogFileName=unit-$(git rev-parse --short HEAD).trx" --results-directory docs/evidence

# 3. Real-PostgreSQL integration tests: AC-04 and AC-10 live here (needs Docker)
dotnet test tests/Celd.Tests --filter "FullyQualifiedName~Integration" \
  --logger "trx;LogFileName=integration-$(git rev-parse --short HEAD).trx" --results-directory docs/evidence

# 4. Database mechanism probe (optional cross-check)
db-probe/run-probe.sh | tee docs/evidence/probe-$(git rev-parse --short HEAD).txt

# 5. Client
cd client && npm install && npm run typecheck && npm run build
```

Then run `docs/acceptance-procedure.md` by hand with a witness and fill in its "Observed" column.

## 4. Acceptance criteria → automated tests

All rows are **Not run** until step 3 above is executed. Fill in the result and commit SHA when it is.

| AC | Requirement focus | Tests | Tier | Result |
| --- | --- | --- | --- | --- |
| AC-01 | Catalog, search, empty, missing | `Visitor_lists_seeded_catalog_with_availability`, `Catalog_search_and_category_filter_including_empty_result`, `Missing_item_is_a_controlled_not_found`, unit `Catalog_search_is_case_insensitive...` | Integration + Unit | Not run |
| AC-02 | Sign in/out, bad credentials, ended session | `Valid_accounts_sign_in_and_see_their_role` (×2), `Wrong_password_and_unknown_user_get_identical_responses`, `Ended_session_cannot_be_replayed`, `Session_cookie_is_HttpOnly_Secure_and_SameSite_Strict`, `Visitors_get_401_and_staff_get_403_when_reserving` | Integration | Not run |
| AC-03 | Reserve; item unavailable to others | `Reserved_item_becomes_unavailable_to_another_student`, unit `Reserve_available_item...`, `Reserve_unavailable_item...` | Both | Not run |
| **AC-04** | **Competing reservations** | `Parallel_reserve_requests_yield_exactly_one_active_reservation` (20 parallel), `Database_index_rejects_a_competitor_that_slipped_past_the_availability_check` (forced race), `Database_refuses_a_second_active_reservation_inserted_directly` | **Integration, real PostgreSQL** | Not run |
| AC-05 | No access to another's record | `Student_cannot_read_or_cancel_another_students_reservation` (HTTP + unit), `Reservation_owner_comes_from_the_session_not_the_request_body` | Both | Not run |
| AC-06 | Cancel own | `Owner_cancels_and_record_stays_in_history_and_item_is_available`, unit `Owner_cancels_Reserved...` | Both | Not run |
| AC-07 | Staff-only pickup | `Students_and_visitors_cannot_record_pickup_or_return_or_see_the_staff_queue`, `Staff_queue_pickup_return_and_item_can_be_reserved_again`, unit `Students_and_visitors_cannot_record...` | Both | Not run |
| AC-08 | Return; reserve again | `Staff_queue_pickup_return_and_item_can_be_reserved_again`, unit `Staff_records_pickup_then_return...` | Both | Not run |
| AC-09 | Invalid, repeated, stale | `Invalid_and_repeated_transitions_are_conflicts_with_no_extra_history`, `Stale_pickup_is_rejected_when_the_reservation_changed_underneath_it`, `Parallel_cancel_and_pickup_accept_exactly_one`, unit `Repeated_and_out_of_order...`, all 24 cases of `Only_the_three_SRS_transitions_are_allowed` | Both | Not run |
| **AC-10** | **History per change; forced failure leaves nothing** | `Full_lifecycle_records_one_event_per_change_with_actor_and_time`, `Failed_history_write_rolls_back_the_new_reservation`, `Failed_history_write_rolls_back_the_status_change`, `Records_survive_an_application_restart` | **Integration, real PostgreSQL** | Not run |
| AC-11 | UI states, keyboard | Manual procedure part B | Manual | Not run |
| AC-12 | Tests linked to IDs, with revision | This file + RTM, once results exist | Process | Not met yet |
| AC-13 – AC-16 | Docs, CI, deploy, performance | Weeks 3–4; perf procedure part C | — | Not started |

## 5. Trace of one reservation: React → API → database → history (Day 3)

Follow `student1` reserving the projector. Each step names the file and line where it happens.

1. **Click Reserve** — `client/src/pages/ItemPage.tsx:21` calls `api.reserve(itemId)`.
2. **HTTP request** — `client/src/api.ts:93` sends `POST /api/reservations` with body `{ equipmentItemId }` **only**, plus the `X-CSRF-TOKEN` header and the `celd.session` cookie.
3. **Session check** — `server/Celd.Api/Auth/SessionCookieEvents.cs:16` confirms the session row is still open. If it was revoked, the request stops here with 401.
4. **Role check** — `[Authorize(Roles = "Student")]` on `ReservationsController.Create` (`Controllers/ReservationsController.cs:20`). Staff get 403 here.
5. **CSRF check** — `Auth/CsrfProtectionFilter.cs` validates the header. A missing token gets 400 `csrf_failed`.
6. **Controller** — `ReservationsController.cs:23` passes only the item id to the Central Application Service.
7. **Central Application Service** — `Application/CentralApplicationService.cs:31-32` reads the caller from the session (`Auth/CurrentUser.cs:41`), rechecks the Student role, and delegates.
8. **Reservation Service** — `Application/ReservationService.cs:15` checks the item exists, then runs a friendly availability check at line 24. That check is not the guarantee.
9. **History** — line 42 calls `HistoryOperations.Record` (`Application/HistoryOperations.cs:14`), which **adds** a `Created` event to the same DbContext and does not save.
10. **One transaction** — line 47 `SaveChangesAsync` inserts the `Reservations` row and the `HistoryEvents` row together.
11. **Database guarantee** — PostgreSQL checks `IX_Reservations_OneActivePerItem` (`Data/CeldDbContext.cs:65`). If another request committed first, the insert fails, and line 49 turns that into 409 `item_unavailable` with nothing saved.
12. **Response** — 201 with the reservation. The client then reloads the item from the server (`ItemPage.tsx`, `finally` block), so the "Unavailable" badge comes from the database, not from a guess.

To watch it live: run the API with `"Microsoft.EntityFrameworkCore.Database.Command": "Information"` in `appsettings.Development.json`. The log shows the INSERTs inside one transaction.

## 6. Smells, trade-offs and unverified claims (for Day 4)

| # | Finding | Why it matters | Proposed action |
| --- | --- | --- | --- |
| F1 | **Nothing in C# has been compiled or run yet.** | Every AC claim is currently unverified. | Day 3: build, run both tiers, record SHAs. Top priority. |
| F2 | Role checks exist twice: controller attributes and `CentralApplicationService.Require`. | Duplication. If they disagree, behaviour depends on the entry point. | Keep deliberately (defence in depth) and test both layers, which the tests do. Discuss at Day 4. |
| F3 | `CentralApplicationService` is a pass-through for most calls. | Profile B's risk: a layer that adds indirection without value. | Its value is the single place that resolves the caller and rechecks roles. If it stays this thin by Week 3, consider ADR-003 to merge it into controllers. |
| F4 | Availability check before insert duplicates the index. | Two sources of truth for one rule. | Keep: the check gives a clean 409 in the common case; the index is the guarantee. Documented in code comments. |
| F5 | `IInterceptor` seam in `Program.cs` exists for tests. | Production code shaped by tests. | Small and inert in production; recorded in ADR-002. |
| F6 | `EnsureCreated` instead of migrations. | Cannot evolve a deployed schema. | Generate an initial migration before Week 4 deployment. |
| F7 | Data Protection keys not persisted. | Server restart signs everyone out; multiple instances break sessions. | Configure key persistence before deployment. |
| F8 | History order relies on timestamps only. | Two events in the same microsecond would have undefined order. | Low risk (events come from separate requests). Add a sequence column if it ever shows up. |
| F9 | Unit tests use SQLite, production uses PostgreSQL. | SQLite can differ (types, concurrency). | That is why AC-04 and AC-10 are in the PostgreSQL tier only. Unit tier covers rules, not concurrency. |
| F10 | Package versions were pinned from memory or search, not restored. | A version may not exist, or may be incompatible. | First `dotnet restore` / `npm install` will tell. Commit the lock files it creates. |

## 7. Day 4 questions — honest answers as of this file

1. **Which FR/BR/AC items have executed evidence?** None at application level yet. Only the database *mechanisms* behind BR-RES-002, BR-RES-004 and the stale-update check have executed evidence (the probe). Update this answer after Day 3 runs.
2. **AC-04 and AC-05?** Designed and tested in code: a real-PostgreSQL race test with a forced, guaranteed race, plus an ownership test returning 404. Status: **not run**.
3. **AC-07, AC-09, AC-10?** Same: tests exist, including a database trigger that forces the history write to fail mid-transaction. Status: **not run**.
4. **Smells to revise?** F1 first, then F3 (is the central service earning its keep?) and F6/F7 before deployment.
