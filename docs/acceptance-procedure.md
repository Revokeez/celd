# Manual acceptance procedure — NFR-TEST-002, NFR-USAB-001, NFR-PERF-001

A witnessed, documented manual procedure (acceptable under NFR-TEST-002 until an automated browser test exists).
Fill in **Observed** and **Pass/Fail** as you go. A blank row is "not run", never "pass".

| Field | Value |
| --- | --- |
| Commit SHA (`git rev-parse --short HEAD`) | |
| Date / time | |
| Environment (OS, browser, .NET, Node, PostgreSQL) | |
| Performed by | |
| Witnessed by | |

Start from a fresh database: `docker compose down -v && docker compose up -d`, then start the server and client (README section 4).
Use two browser windows: a normal one and a private one, so two users can be signed in at once.

## Part A — Reserve → Pickup → Return, plus a negative path

| # | Step | Expected | Observed | Pass/Fail |
| --- | --- | --- | --- | --- |
| A1 | Open the client without signing in. | Catalog lists at least 6 items, each with name, category, asset code and the word "Available". | | |
| A2 | Search `CAMERA`. Then search `zzz`. | Two camera items. Then the message "No equipment matches your search…". | | |
| A3 | Window 1: sign in as `student1`. | Header shows "Student One (Student)" and a "My Reservations" link. | | |
| A4 | Open "Portable Projector" and press **Reserve**. | "Done: Reserved." Availability changes to "Unavailable". | | |
| A5 | **Negative path.** Window 2: sign in as `student2`, open the projector. | "Unavailable", and no Reserve button. | | |
| A6 | Window 2: open the browser dev tools console and run `fetch('/api/reservations',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({equipmentItemId:'<projector id from the URL>'})}).then(r=>r.status)` | `400` (no CSRF token). Nothing changes. | | |
| A7 | Window 2: sign out, sign in as `staff1`, open **Staff Reservations**, filter "Reserved". | One row: Portable Projector, Student One, Reserved. | | |
| A8 | Open it and press **Record pickup**. | "Done: Pickup recorded." Status "Checked out". History shows Created, PickedUp. | | |
| A9 | Press **Record return**. | Status "Returned". History shows Created, PickedUp, Returned with names and times. | | |
| A10 | Window 1 (`student1`): reload the projector page. | "Available" again. | | |
| A11 | Window 1: open **My Reservations**. | The projector reservation appears under "Completed or cancelled" as "Returned". | | |
| A12 | Restart the API server (Ctrl+C, `dotnet run`), then reload My Reservations (sign in again if asked). | The returned reservation and its history are still there. | | |

## Part B — Usability and failure states (AC-11, NFR-USAB-001)

| # | Step | Expected | Observed | Pass/Fail |
| --- | --- | --- | --- | --- |
| B1 | Using only the keyboard (Tab, Shift+Tab, Enter): sign in, search, open an item, reserve it, cancel it. | Every control is reachable and has a visible focus outline. | | |
| B2 | Look at availability and status badges. | Each shows a word (and ✓/✕), not only a colour. | | |
| B3 | Stop the API server, then reload the catalog. | An error message with a "Try again" button; no fake results. | | |
| B4 | Sign in with a wrong password. | "Invalid user name or password." The message does not say which one was wrong. | | |
| B5 | Visit `#/items/00000000-0000-0000-0000-000000000000`. | "This item does not exist or is no longer listed." | | |

## Part C — Performance (AC-16, NFR-PERF-001)

1. Set `"Seed:PerformanceData": true` in `appsettings.Development.json`. Drop the database and restart, so the server seeds at least 50 items and 100 historical reservations.
2. Warm up: request each URL once and ignore the result.
3. Time 10 requests of each, signed in where needed (paste the session cookie from the browser):

```bash
for i in $(seq 10); do curl -s -o /dev/null -w "%{time_total}\n" http://localhost:5080/api/equipment; done
for i in $(seq 10); do curl -s -o /dev/null -w "%{time_total}\n" -H "Cookie: celd.session=<value>" http://localhost:5080/api/staff/reservations; done
```

| Request | Items / reservations | Slowest of 10 (s) | Limit | Pass/Fail |
| --- | --- | --- | --- | --- |
| `GET /api/equipment` | | | 2.0 s | |
| `GET /api/staff/reservations` | | | 2.0 s | |

Record the machine (CPU, RAM) with the results.
