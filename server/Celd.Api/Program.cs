using System.Text.Json.Serialization;
using Celd.Api.Application;
using Celd.Api.Auth;
using Celd.Api.Data;
using Celd.Api.Domain;
using Celd.Api.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var cookieSecurity = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

// ---- Entity Framework Core + relational database -------------------------------------------
// The connection string is read lazily so tests and deployment can supply it through configuration.
// Any IInterceptor registered in DI is attached; production registers none (tests use this seam).
builder.Services.AddDbContext<CeldDbContext>((sp, options) =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:Celd is not configured. See README, 'Configure secrets'.");
    options.UseNpgsql(connectionString);
    options.AddInterceptors(sp.GetServices<IInterceptor>());
});

// ---- Authentication & Authorization -----------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<SessionCookieEvents>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "celd.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = cookieSecurity;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.EventsType = typeof(SessionCookieEvents);
    });
builder.Services.AddAuthorization();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "celd.csrf";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = cookieSecurity;
});

// ---- Central Application Service and domain services ----------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<HistoryOperations>();
builder.Services.AddScoped<EquipmentService>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddScoped<CentralApplicationService>();

// ---- API Controllers ---------------------------------------------------------------------------
// [AutoValidateAntiforgeryToken]'s filter lives in MVC's view-features services, which AddControllers() omits.
builder.Services.AddMvcCore().AddViews();
// CSRF is enforced by [AutoValidateAntiforgeryToken] on the controllers; this only formats its failures as csrf_failed.
builder.Services.AddSingleton<IClientErrorFactory, CeldClientErrorFactory>();
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options =>
    {
        // Model validation failures use the same problem shape as every other error (FR-ERR-001).
        options.InvalidModelStateResponseFactory = context =>
        {
            var detail = string.Join(" ", context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .Select(e => $"{e.Key}: {e.Value!.Errors[0].ErrorMessage}"));
            return Problems.ToResult(OperationError.Validation("invalid_request", detail));
        };
    });

// Unhandled exceptions (for example a database failure) become a 500 problem with no internal details.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        if (context.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
        {
            context.ProblemDetails.Detail = "The operation did not complete. No changes were saved. Please refresh and try again.";
            context.ProblemDetails.Extensions["category"] = "ServerError";
            context.ProblemDetails.Extensions["errorCode"] = "server_error";
        }
    });

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ---- Database creation and seed (SRS §2.3) ------------------------------------------------------
// Known limitation: EnsureCreated, not migrations. See docs/adr/ADR-002 and README "Known limitations".
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CeldDbContext>();
    await db.Database.EnsureCreatedAsync();

    if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");
        await DbSeeder.SeedAsync(db, app.Configuration, logger);
    }
}

app.Run();

/// <summary>Exposed so the integration tests can host the API in memory.</summary>
public partial class Program;
