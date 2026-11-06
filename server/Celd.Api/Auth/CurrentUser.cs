using System.Security.Claims;
using Celd.Api.Domain;

namespace Celd.Api.Auth;

/// <summary>Who is calling, derived only from the validated session (BR-AUT-001).</summary>
public sealed record CurrentUser(Guid Id, Role Role, string DisplayName);

public interface ICurrentUserAccessor
{
    /// <summary>Returns null for a Visitor (no valid session).</summary>
    CurrentUser? Get();
}

public static class AuthClaims
{
    public const string SessionId = "celd:sid";

    public static CurrentUser? ToCurrentUser(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;

        return Guid.TryParse(id, out var userId) && Enum.TryParse<Role>(role, out var parsedRole)
            ? new CurrentUser(userId, parsedRole, principal.FindFirst(ClaimTypes.Name)?.Value ?? "")
            : null;
    }
}

/// <summary>
/// Reads identity from the cookie principal that the authentication middleware already validated.
/// Request bodies and query strings are never consulted.
/// </summary>
public sealed class HttpCurrentUserAccessor(IHttpContextAccessor http) : ICurrentUserAccessor
{
    public CurrentUser? Get() => AuthClaims.ToCurrentUser(http.HttpContext?.User);
}
