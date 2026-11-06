using System.Security.Claims;
using Celd.Api.Application;
using Celd.Api.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Celd.Api.Auth;

/// <summary>
/// Hooks the cookie handler so that:
/// 1. every request checks the session is still open in the database (FR-AUT-002), and
/// 2. API callers get 401/403 problem responses instead of HTML login redirects (FR-ERR-001).
/// </summary>
public sealed class SessionCookieEvents(AuthService auth) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var raw = context.Principal?.FindFirst(AuthClaims.SessionId)?.Value;
        var valid = Guid.TryParse(raw, out var sessionId)
            && await auth.IsSessionActiveAsync(sessionId, context.HttpContext.RequestAborted);

        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) =>
        Problems.WriteAsync(context.HttpContext, OperationError.Unauthenticated());

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) =>
        Problems.WriteAsync(context.HttpContext, OperationError.Forbidden());
}
