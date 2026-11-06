using System.Security.Claims;
using Celd.Api.Data;
using Celd.Api.Domain;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Celd.Api.Auth;

/// <summary>Sign-in and sign-out (FR-AUT-001, FR-AUT-002) with a server-side session record.</summary>
public sealed class AuthService(CeldDbContext db, IPasswordHasher<User> hasher, TimeProvider clock)
{
    /// <summary>
    /// Returns a principal on success, or null for any credential failure. Callers must not reveal
    /// whether the user name or the password was wrong.
    /// </summary>
    public async Task<ClaimsPrincipal?> SignInAsync(string userName, string password, CancellationToken ct)
    {
        var normalized = userName.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == normalized, ct);

        if (user is null)
        {
            // Hash anyway so an unknown user takes about as long as a wrong password.
            hasher.HashPassword(new User(), password);
            return null;
        }

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var session = new UserSession { Id = Guid.NewGuid(), UserId = user.Id, CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);

        var claims = new Claim[]
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(AuthClaims.SessionId, session.Id.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        return new ClaimsPrincipal(identity);
    }

    public async Task RevokeSessionAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirst(AuthClaims.SessionId)?.Value, out var sessionId))
        {
            return;
        }

        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is { RevokedAtUtc: null })
        {
            session.RevokedAtUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
    }

    public Task<bool> IsSessionActiveAsync(Guid sessionId, CancellationToken ct) =>
        db.UserSessions.AnyAsync(s => s.Id == sessionId && s.RevokedAtUtc == null, ct);
}
