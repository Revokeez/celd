using Celd.Api.Application;
using Celd.Api.Http;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Celd.Api.Auth;

/// <summary>
/// NFR-SEC-002. Every state-changing request must carry the antiforgery token in the X-CSRF-TOKEN header.
/// Runs after the authorization middleware, so a Visitor still gets 401 before a CSRF error.
/// Failures use the same problem shape as every other error (FR-ERR-001).
/// </summary>
public sealed class CsrfProtectionFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return;
        }

        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            context.Result = Problems.ToResult(OperationError.Validation("csrf_failed", "Missing or invalid CSRF token. Refresh the page and try again."));
        }
    }
}
