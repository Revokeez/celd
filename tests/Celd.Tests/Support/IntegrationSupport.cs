using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Celd.Api.Application;
using Celd.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Celd.Tests.Support;

/// <summary>
/// One real PostgreSQL server for the whole integration run. Each test gets its own fresh database.
/// Uses Docker via Testcontainers, or an existing server when CELD_TEST_POSTGRES is set
/// (for example a GitHub Actions service container).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _adminConnectionString = "";

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("CELD_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _adminConnectionString = external;
            return;
        }

        _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await _container.StartAsync();
        _adminConnectionString = _container.GetConnectionString();
    }

    public async Task<string> CreateDatabaseAsync()
    {
        var name = "celd_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>Test-only seed passwords, generated per run. Nothing here is a real credential.</summary>
public static class TestUsers
{
    public static readonly string Password = "T3st!" + Guid.NewGuid().ToString("N");

    public const string Student1 = "student1";
    public const string Student2 = "student2";
    public const string Staff = "staff1";
}

/// <summary>
/// Runs one SQL statement on a separate connection just before the API's next matching SaveChanges.
/// Used to create deterministic races: "another request committed between our check and our write".
/// </summary>
public sealed class InjectBeforeSaveInterceptor(string connectionString) : SaveChangesInterceptor
{
    private (Func<DbContext, bool> When, string Sql)? _armed;

    public void Arm(Func<DbContext, bool> when, string sql) => _armed = (when, sql);

    public bool Fired { get; private set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (_armed is { } armed && eventData.Context is { } context && armed.When(context))
        {
            _armed = null;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(armed.Sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            Fired = true;
        }

        return result;
    }
}

public sealed class CeldApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public InjectBeforeSaveInterceptor Interceptor { get; } = new(connectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Celd"] = connectionString,
            ["Seed:Enabled"] = "true",
            ["Seed:Users:0:UserName"] = TestUsers.Student1,
            ["Seed:Users:0:DisplayName"] = "Student One",
            ["Seed:Users:0:Role"] = "Student",
            ["Seed:Users:0:Password"] = TestUsers.Password,
            ["Seed:Users:1:UserName"] = TestUsers.Student2,
            ["Seed:Users:1:DisplayName"] = "Student Two",
            ["Seed:Users:1:Role"] = "Student",
            ["Seed:Users:1:Password"] = TestUsers.Password,
            ["Seed:Users:2:UserName"] = TestUsers.Staff,
            ["Seed:Users:2:DisplayName"] = "Desk Staff",
            ["Seed:Users:2:Role"] = "Staff",
            ["Seed:Users:2:Password"] = TestUsers.Password,
        }));
        builder.ConfigureTestServices(services => services.AddSingleton<IInterceptor>(Interceptor));
    }
}

/// <summary>A browser-like client: keeps its own cookies and sends the CSRF header on POSTs.</summary>
public sealed class ApiClient(HttpClient http)
{
    private string? _csrf;

    public HttpClient Http => http;

    public async Task RefreshCsrfAsync()
    {
        var token = await http.GetFromJsonAsync<CsrfTokenDto>("/api/auth/csrf");
        _csrf = token!.Token;
    }

    public Task<HttpResponseMessage> GetAsync(string path) => http.GetAsync(path);

    public async Task<HttpResponseMessage> PostAsync(string path, object? body = null, bool withCsrf = true)
    {
        if (withCsrf && _csrf is null)
        {
            await RefreshCsrfAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };
        if (withCsrf)
        {
            request.Headers.Add("X-CSRF-TOKEN", _csrf);
        }

        return await http.SendAsync(request);
    }

    public async Task<HttpResponseMessage> LoginAsync(string userName, string password)
    {
        await RefreshCsrfAsync();
        var response = await PostAsync("/api/auth/login", new { userName, password });

        // The CSRF token is bound to identity, so a new one is needed after signing in.
        await RefreshCsrfAsync();
        return response;
    }
}

[Collection(PostgresCollection.Name)]
public abstract class IntegrationTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected CeldApiFactory Factory { get; set; } = null!;

    protected string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        ConnectionString = await postgres.CreateDatabaseAsync();
        Factory = new CeldApiFactory(ConnectionString);
        _ = Factory.Server; // Starts the app: creates the schema and seeds users and items.
    }

    public async Task DisposeAsync() => await Factory.DisposeAsync();

    protected static readonly Uri BaseUri = new("https://localhost");

    /// <summary>A client with its own cookie jar, like a separate browser. HTTPS so Secure cookies are sent.</summary>
    protected ApiClient NewClient(CookieContainer? cookies = null) =>
        new(Factory.CreateDefaultClient(BaseUri, new CookieContainerHandler(cookies ?? new CookieContainer())));

    protected async Task<ApiClient> SignedInAsync(string userName)
    {
        var client = NewClient();
        var response = await client.LoginAsync(userName, TestUsers.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    protected CeldDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CeldDbContext>().UseNpgsql(ConnectionString).Options);

    protected async Task<Guid> ItemIdAsync(string assetCode)
    {
        await using var db = NewDb();
        return await db.EquipmentItems.Where(i => i.AssetCode == assetCode).Select(i => i.Id).SingleAsync();
    }

    protected async Task<Guid> UserIdAsync(string userName)
    {
        await using var db = NewDb();
        return await db.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync();
    }

    protected static async Task<ReservationDto> ReserveOkAsync(ApiClient client, Guid itemId)
    {
        var response = await client.PostAsync("/api/reservations", new { equipmentItemId = itemId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReservationDto>())!;
    }

    /// <summary>Asserts the FR-ERR-001 problem shape and returns its errorCode.</summary>
    protected static async Task<string> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string expectedCategory)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCategory, json.RootElement.GetProperty("category").GetString());
        return json.RootElement.GetProperty("errorCode").GetString()!;
    }
}
