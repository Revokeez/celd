using Celd.Api.Application;
using Microsoft.AspNetCore.Mvc;

namespace Celd.Api.Http;

/// <summary>
/// FR-ERR-001: one error shape for the whole API. Every error is application/problem+json with
/// `status`, `title`, `detail`, plus `category` (which of the five kinds) and `errorCode` (machine-readable).
/// No stack traces or internal details are ever included.
/// </summary>
public static class Problems
{
    public static ProblemDetails Create(OperationError error)
    {
        var status = StatusFor(error.Kind);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(error.Kind),
            Detail = error.Message,
            Type = $"https://httpstatuses.io/{status}",
        };
        problem.Extensions["category"] = error.Kind.ToString();
        problem.Extensions["errorCode"] = error.Code;
        return problem;
    }

    public static ObjectResult ToResult(OperationError error)
    {
        var problem = Create(error);
        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" },
        };
    }

    public static Task WriteAsync(HttpContext context, OperationError error)
    {
        var problem = Create(error);
        context.Response.StatusCode = problem.Status!.Value;
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }

    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthenticated => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => "The request is not valid.",
        ErrorKind.Unauthenticated => "Authentication required.",
        ErrorKind.Forbidden => "Not permitted.",
        ErrorKind.NotFound => "Not found.",
        ErrorKind.Conflict => "The request conflicts with the current state.",
        _ => "Unexpected error.",
    };
}

public static class ControllerResultExtensions
{
    public static IActionResult ToActionResult<T>(this OperationResult<T> result, Func<T, IActionResult>? onSuccess = null) =>
        result.Succeeded
            ? onSuccess?.Invoke(result.Value!) ?? new OkObjectResult(result.Value)
            : Problems.ToResult(result.Error!);
}
