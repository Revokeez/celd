# ADR-003: CSRF protection declared by annotation only

**Status:** Accepted (Week 3), following the instructor's verbal feedback after the Week 2 presentation, as interpreted and agreed by the team.
**Date:** 2026-11-20

## Context

The instructor asked us to "remove the antiforgery from the service". NFR-SEC-002 still requires CSRF protection, so we read this as: stop configuring antiforgery as service-level code in `Program.cs`, and rely on the declarative attribute on the controllers.

Before this change, CSRF involved three pieces:

1. `[AutoValidateAntiforgeryToken]` on each controller: the actual check.
2. A custom `AddAntiforgery(...)` block in `Program.cs`: header `X-CSRF-TOKEN`, cookie `celd.csrf`, `SameSite=Strict`.
3. A custom `IClientErrorFactory` (`Http/CeldClientErrorFactory.cs`) that turned a failed check into our `csrf_failed` problem body.

## Decision

- Keep **`[AutoValidateAntiforgeryToken]`** on `AuthController`, `ReservationsController` and `StaffReservationsController`. This is the only place CSRF is declared.
- Remove the `AddAntiforgery(...)` configuration and use ASP.NET Core's defaults: header `RequestVerificationToken`, cookie `.AspNetCore.Antiforgery.*`, which is `HttpOnly` and `SameSite=Strict` by default.
- Remove `CeldClientErrorFactory`. A failed check now returns ASP.NET Core's standard 400 problem body.
- Keep `AddMvcCore().AddViews()`. The attribute is only a marker, and the filter that enforces it is registered by MVC's view features. Without this line the attribute would not run.
- Keep `IAntiforgery` in `AuthController.GetCsrfToken`. Something has to issue the token.

## Consequences

- (+) CSRF is visible where it applies: one attribute per controller, with no custom configuration to read.
- (+) Protection is unchanged. A POST without a valid token is still refused, and `State_changing_request_without_csrf_token_is_refused` still covers it.
- (−) A CSRF failure no longer has the FR-ERR-001 `category` / `errorCode` fields. The client shows a generic message for it. This is acceptable because the client always sends the token, so a real user should never see it.
- (−) The header name changed. `client/src/api.ts`, the test client and the README were updated. Any custom script must send `RequestVerificationToken`.
- (−) The antiforgery cookie no longer forces `Secure`. The session cookie still does, and the token is useless without the session.

## Alternatives considered

| Alternative | Why not chosen |
| --- | --- |
| Remove CSRF protection entirely | Breaks NFR-SEC-002. `SameSite=Strict` alone leaves the body-less POSTs (`cancel`, `pickup`, `return`) exposed to same-site attackers and to older browsers. |
| Custom attribute that requires a custom header (no token) | Needs no antiforgery service, but relies only on CORS staying closed. Weaker than a token, so kept as a fallback if the instructor wants zero antiforgery services. |
