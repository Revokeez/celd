using Celd.Api.Application;
using Celd.Api.Auth;
using Celd.Api.Http;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Celd.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AutoValidateAntiforgeryToken] // NFR-SEC-002: login and logout need the X-CSRF-TOKEN header.
public sealed class AuthController(AuthService auth, IAntiforgery antiforgery) : ControllerBase
{
    /// <summary>
    /// Issues a CSRF token for the current identity. The client must fetch a fresh one after
    /// signing in or out, because the token is bound to who is signed in.
    /// </summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    public ActionResult<CsrfTokenDto> GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return new CsrfTokenDto(tokens.RequestToken!);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var principal = await auth.SignInAsync(request.UserName, request.Password, ct);
        if (principal is null)
        {
            // Same response for unknown user and wrong password (FR-AUT-001).
            return Problems.ToResult(new OperationError(ErrorKind.Unauthenticated, "invalid_credentials", "Invalid user name or password."));
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return Ok(ToDto(AuthClaims.ToCurrentUser(principal)!));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.RevokeSessionAsync(User, ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>Who am I? The client uses this for role-appropriate navigation only (FR-AUT-003).</summary>
    [HttpGet("me")]
    [Authorize]
    public ActionResult<CurrentUserDto> Me() => ToDto(AuthClaims.ToCurrentUser(User)!);

    private static CurrentUserDto ToDto(CurrentUser user) => new(user.Id, user.DisplayName, user.Role.ToString());
}
