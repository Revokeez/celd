# ADR-002: PostgreSQL, server-side sessions, and a two-tier test strategy

**Status:** Proposed (Week 2, Day 3) — team to confirm. Builds on ADR-001; does not change its architecture.
**Date:** 2026-11-06

## Context

Implementing ADR-001 forced four decisions that Week 1 left open:

1. **Which database (SRS question Q3).** The filtered-index syntax and the provider-faithful tests depend on it. The instructor environment sheet has not yet confirmed a provider to us.
2. **How to end a session (FR-AUT-002).** A plain ASP.NET Core auth cookie stays valid until it expires, even after sign-out. A copied cookie would still work, which violates "prevent continued use of that ended session".
3. **How to test under Profile B.** Profile B emphasises service-unit tests. But mocks, and EF's in-memory provider, enforce no unique index and no transaction, so they cannot prove BR-RES-002 or BR-RES-004.
4. **Schema creation.** EF migrations need the .NET CLI to generate, which was not available when this code was written.

## Decision

1. **PostgreSQL 16** via Npgsql. The index filter `"Status" IN ('Reserved', 'CheckedOut')` is written in one place (`CeldDbContext.OneActivePerItemFilter`). Changing provider means changing that string, the package, and `DbErrors`.
2. **Server-side sessions.** Sign-in writes a `UserSessions` row; its id is a cookie claim. Every request checks the row is still open; sign-out revokes it.
3. **Two test tiers**:
   - *Service-unit* (Profile B emphasis): `CentralApplicationService` and `ReservationService` on SQLite in-memory, a real relational engine with indexes, foreign keys and concurrency tokens. Fast; no Docker.
   - *Integration* (required for AC-04 and AC-10 regardless of profile): the full API in memory against **real PostgreSQL** via Testcontainers, fresh database per test. Races are forced deterministically with a `SaveChanges` interceptor; persistence failure is forced with a database trigger.
4. **`EnsureCreated` for now**, with migrations planned before deployment.

## Consequences

- (+) AC-04 and AC-10 are tested on the real provider, including a race that is guaranteed to happen, not just hoped for.
- (+) Sign-out is real: a stolen or stale cookie stops working immediately.
- (−) One extra indexed query per authenticated request (session lookup).
- (−) Integration tests need Docker or an existing PostgreSQL server.
- (−) The `IInterceptor` registration in `Program.cs` is a small seam that exists for tests. Production registers none.
- (−) `EnsureCreated` cannot evolve an existing database. Must move to migrations before Week 4 deployment.

## Alternatives considered

| Alternative | Why not chosen |
| --- | --- |
| SQL Server / Azure SQL | Equally valid; switch if the environment sheet requires it. Needs the filter string changed to `[Status] IN (...)` and SQL Server error codes in `DbErrors`. |
| ASP.NET Core Identity | Heavier than needed for three seeded accounts with no registration; still needs a security-stamp check to meet FR-AUT-002. |
| EF in-memory provider for service tests | Enforces no unique index and no transactions; it would pass tests that real PostgreSQL fails. |
| Only parallel HTTP requests to test AC-04 | Can pass by luck if the requests happen not to overlap. Kept, but backed by a deterministic injected race. |
