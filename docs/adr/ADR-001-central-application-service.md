# ADR-001: Central application service with database-enforced reservation invariant

**Status:** Accepted for Week 2 implementation (proposed Day 1). This is the original Week 1 text, kept visible.
Later changes go in new ADRs (see ADR-002), not edits here.

## Context

CELD has five use cases that share the same critical rules: server-derived identity, ownership,
one active reservation per item, legal transitions only, and atomic history. The course stack is fixed:
React/TypeScript, ASP.NET Core Web API, EF Core, a relational database and cookie sessions.
Group 2 starts from Profile B.

## Decision

1. Controllers stay thin. Every use case goes through one Central Application Service, which delegates to the Equipment Service and the Reservation Service.
2. Authentication & Authorization derives the current user from the validated cookie session. No endpoint accepts a user id or role from the caller.
3. The Reservation Service holds the transition table and ownership checks in one place.
4. BR-RES-002 is guaranteed by the database through a filtered unique index on active reservations. The service-level availability check exists only for a clear error message.
5. Each mutation and its History Event are saved in a single SaveChanges call, so they commit or fail together.
6. A concurrency token on Reservation rejects stale transitions such as a cancel and a pickup arriving at the same time.
7. Availability is computed from active reservations, not stored as a separate flag, so it cannot drift (DR-006).

## Consequences

- (+) Every rule has exactly one home, which keeps the RTM short and defensible.
- (+) Service methods can be unit-tested without HTTP.
- (+) The uniqueness guarantee holds even if a future code path skips the service check.
- (−) Risk that the Central Application Service grows into a god class. Mitigation: it only orchestrates and contains no rules.
- (−) Service-unit tests with mocks cannot prove concurrency or atomicity. Integration tests against the real provider are mandatory (NFR-REL-001).
- (−) Filtered index syntax depends on the database provider.

## Alternatives considered

| Alternative | Why it could be better | Why not chosen |
| --- | --- | --- |
| Profile D: feature modules | Higher cohesion per use case | Transition and ownership rules risk duplication across handlers |
| Profile F: minimal direct handlers | Least code | Rules spread into endpoint code, harder to unit-test |
| Serializable transaction or row lock instead of a filtered unique index | Works on providers without partial indexes | More deadlock/retry handling; correctness depends on every code path using it |
