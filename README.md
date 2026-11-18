# Campus Equipment Lending Desk (CELD) — Group 2

Students reserve campus equipment; lending-desk staff record pickup and return.
Built to CELD SRS v1.1.1 with **Profile B**: a Central Application Service in front of
an Equipment Service and a Reservation Service, with service-unit tests as the main test style.

| Part | Technology | Folder |
| --- | --- | --- |
| Client | React 19 + TypeScript, Vite | `client/` |
| Server | ASP.NET Core Web API, .NET 10 | `server/Celd.Api/` |
| Persistence | Entity Framework Core 10 + PostgreSQL 16 | `server/Celd.Api/Data/` |
| Tests | xUnit, SQLite in-memory (service-unit), Testcontainers PostgreSQL (integration) | `tests/Celd.Tests/` |
| DB mechanism probe | Plain SQL against a throwaway PostgreSQL | `db-probe/` |

> **Status (Week 2, 2026-11-06):** code complete for UC-001 to UC-005. The C# build and test suite have
> **not been run yet**. See [docs/week2-evidence.md](docs/week2-evidence.md) for exactly what has and has not been executed.

---

## 1. Prerequisites

| Tool | Version | Check |
| --- | --- | --- |
| .NET SDK | 10.0.x (confirm against the instructor environment sheet) | `dotnet --version` |
| Node.js | 22 LTS | `node --version` |
| Docker | Any recent version, running | `docker info` |

Docker runs PostgreSQL for local development and for the integration tests.
No Docker? Point both at any PostgreSQL 16 server instead (see section 3 and section 6).

## 2. Start the database

```bash
docker compose up -d          # PostgreSQL 16 on localhost:5432, data kept in a named volume
```

The compose file reads `POSTGRES_PASSWORD` from your shell or a local `.env` file (git-ignored). Example:

```bash
echo "POSTGRES_PASSWORD=$(openssl rand -hex 16)" > .env
```

## 3. Configure secrets (never committed)

Seed account names and roles are in `appsettings.json`. Their **passwords** and the **connection string**
live in .NET user-secrets on your machine only:

```bash
cd server/Celd.Api
PG=$(grep POSTGRES_PASSWORD ../../.env | cut -d= -f2)
dotnet user-secrets set "ConnectionStrings:Celd" "Host=localhost;Port=5432;Database=celd;Username=celd;Password=$PG"
dotnet user-secrets set "Seed:Users:0:Password" "<choose a password for student1>"
dotnet user-secrets set "Seed:Users:1:Password" "<choose a password for student2>"
dotnet user-secrets set "Seed:Users:2:Password" "<choose a password for staff1>"
```

Share demo passwords with your team through a private channel, never in Git, chat logs, or the README (SRS §2.3).
A seed user with no password is skipped with a warning; there is no default password.

## 4. Run

```bash
# Terminal 1 — API on http://localhost:5080
cd server/Celd.Api
dotnet run

# Terminal 2 — client on http://localhost:5173 (proxies /api to the server)
cd client
npm install        # first time; commit the generated package-lock.json
npm run dev
```

On first start the server creates the schema and seeds 7 equipment items and 3 accounts:

| User name | Role |
| --- | --- |
| `student1` | Student |
| `student2` | Student |
| `staff1` | Lending Staff |

Set `"Seed:PerformanceData": true` to add the 50-item / 100-history data set for NFR-PERF-001.

## 5. How the code maps to the architecture diagram

| Diagram box | Code |
| --- | --- |
| React + TypeScript Client | `client/src/` — `api.ts` (HTTP, CSRF, error types), `pages/` |
| ASP.NET Core API Controllers | `server/Celd.Api/Controllers/` — thin, no rules |
| Authentication & Authorization | `server/Celd.Api/Auth/` — cookie sessions, session revocation, current user |
| Central Application Service | `Application/CentralApplicationService.cs` — entry point, role recheck, delegation |
| Equipment Service | `Application/EquipmentService.cs` — catalog, computed availability |
| Reservation Service | `Application/ReservationService.cs` — create, ownership, transitions, saves |
| History Operations | `Application/HistoryOperations.cs` — builds events, never saves on its own |
| Entity Framework Core | `Data/CeldDbContext.cs` — filtered unique index, concurrency token |
| Relational Database | PostgreSQL 16 |

Where each critical rule is enforced:

| Rule | Where | How |
| --- | --- | --- |
| BR-AUT-001 identity | `Auth/CurrentUser.cs` | User id and role come only from the validated cookie. Request DTOs have no user or role fields. |
| BR-AUT-002 ownership | `ReservationService.CanView` | A Student acting on someone else's reservation gets 404, the same as a missing record. |
| BR-AUT-003 staff-only | `[Authorize(Roles = "Staff")]` + `CentralApplicationService.Require` | Two independent checks. |
| BR-RES-002 one active | `CeldDbContext` filtered unique index `IX_Reservations_OneActivePerItem` | The database rejects the second active row; the service maps it to 409. |
| BR-RES-003 transitions | `ReservationTransitions.Legal` | A three-row table. Everything else is refused. |
| Stale updates | `Reservation.Version` concurrency token | A change based on an outdated read gets 409 `stale_state`. |
| BR-RES-004 atomic history | `ReservationService` + `HistoryOperations` | Change and event are saved in **one** `SaveChanges` call, which is one transaction. |
| FR-AUT-002 sign-out | `Auth/SessionCookieEvents.cs` | Sessions are stored server-side; a copied cookie stops working after sign-out. |
| NFR-SEC-002 CSRF | `[AutoValidateAntiforgeryToken]` on `AuthController`, `ReservationsController`, `StaffReservationsController`; failures formatted by `Http/CeldClientErrorFactory.cs` | Every POST needs the `X-CSRF-TOKEN` header. |

## 6. Test

```bash
dotnet test tests/Celd.Tests                                          # everything (needs Docker)
dotnet test tests/Celd.Tests --filter "FullyQualifiedName~Unit"       # service-unit only, no Docker needed
dotnet test tests/Celd.Tests --filter "FullyQualifiedName~Integration" --logger "trx;LogFileName=integration.trx"
```

The integration tests start a PostgreSQL 16 container and give **each test its own fresh database**.
Without Docker, point them at any PostgreSQL server you can create databases on:

```bash
export CELD_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres;Password=<pw>"
```

To keep evidence (NFR-TEST-001), save the results file with the commit it ran on:

```bash
dotnet test tests/Celd.Tests --logger "trx;LogFileName=$(git rev-parse --short HEAD).trx" --results-directory docs/evidence
```

Database mechanism probe (no .NET needed, needs PostgreSQL server binaries):

```bash
db-probe/run-probe.sh
```

The manual end-to-end procedure (NFR-TEST-002) is in [docs/acceptance-procedure.md](docs/acceptance-procedure.md).

## 7. API summary

All errors are `application/problem+json` with `status`, `title`, `detail`, `category`
(`Validation`, `Unauthenticated`, `Forbidden`, `NotFound`, `Conflict`, or `ServerError`) and `errorCode`.

| Method | Path | Who | Notes |
| --- | --- | --- | --- |
| GET | `/api/equipment?search=&category=` | Anyone | Case-insensitive name search |
| GET | `/api/equipment/categories` | Anyone | |
| GET | `/api/equipment/{id}` | Anyone | 404 `equipment_not_found` |
| GET | `/api/auth/csrf` | Anyone | Fetch again after sign-in/out |
| POST | `/api/auth/login` | Anyone | 401 `invalid_credentials` for any bad credential |
| POST | `/api/auth/logout` | Signed in | Revokes the server-side session |
| GET | `/api/auth/me` | Signed in | |
| POST | `/api/reservations` `{ equipmentItemId }` | Student | 201, or 409 `item_unavailable` |
| GET | `/api/reservations/mine` | Student | |
| GET | `/api/reservations/{id}` and `/history` | Owner or Staff | 404 for other students |
| POST | `/api/reservations/{id}/cancel` | Owning Student | 409 `invalid_transition` / `stale_state` |
| POST | `/api/reservations/{id}/pickup` | Staff | |
| POST | `/api/reservations/{id}/return` | Staff | |
| GET | `/api/staff/reservations?status=` | Staff | 400 `invalid_status_filter` |

There is no DELETE endpoint (BR-RES-008).

## 8. Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `ConnectionStrings:Celd is not configured` | Run the user-secrets commands in section 3. |
| Sign-in always fails | That seed user had no password when the database was first seeded. Set it, then drop the `celd` database (or `docker compose down -v`) and restart. |
| Every POST returns 400 `csrf_failed` | The client must call `/api/auth/csrf` again after sign-in or sign-out. `api.ts` does this; custom scripts must too. |
| Integration tests fail to start | Docker is not running. Start it, or set `CELD_TEST_POSTGRES`. |
| `db-probe/run-probe.sh` loads nothing when run as root | The `postgres` account must be able to read `db-probe/schema.sql`. Run it from a folder that account can read. |

## 9. Known limitations (Week 2)

- **Not yet executed:** the .NET build and the C# test suite. See `docs/week2-evidence.md`.
- **Schema creation uses `EnsureCreated`, not migrations.** Fine for a fresh database; it cannot evolve an existing one. Planned switch to EF migrations before deployment (Week 4). Recorded in ADR-002.
- **Data Protection keys are not persisted.** Restarting the server signs everyone out, and multiple instances would not share sessions. Must be configured before deployment.
- Every authenticated request checks the session table, which costs one indexed query per request.
- The client has no automated browser test yet; NFR-TEST-002 uses the witnessed manual procedure for now.
- The Staff queue defaults to "All statuses" (SRS question Q6 still open).

## 10. Architecture decisions

- [ADR-001](docs/adr/ADR-001-central-application-service.md) — Profile B architecture (Week 1; kept as originally decided).
- [ADR-002](docs/adr/ADR-002-postgresql-sessions-and-test-strategy.md) — PostgreSQL, server-side sessions, two-tier tests (Week 2).
