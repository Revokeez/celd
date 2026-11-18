using Celd.Api.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Celd.Api.Http;

/// <summary>
/// Builds the body for bare client-error results (400, 404, ...) returned under [ApiController].
/// The CSRF check itself is the built-in [AutoValidateAntiforgeryToken] attribute on each controller
/// (NFR-SEC-002); this factory validates nothing. When that attribute rejects a request, it returns
/// the same problem shape every other error uses (FR-ERR-001). Every other client error gets the
/// stock ProblemDetails body, exactly as ASP.NET Core's default factory would produce.
/// </summary>
public sealed class CeldClientErrorFactory(ProblemDetailsFactory problemDetails) : IClientErrorFactory
{
    public IActionResult GetClientError(ActionContext actionContext, IClientErrorActionResult clientError)
    {
        if (clientError is IAntiforgeryValidationFailedResult)
        {
            return Problems.ToResult(OperationError.Validation(
                "csrf_failed", "Missing or invalid CSRF token. Refresh the page and try again."));
        }

        var problem = problemDetails.CreateProblemDetails(actionContext.HttpContext, clientError.StatusCode);
        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json", "application/problem+xml" },
        };
    }
}
