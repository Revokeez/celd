using Celd.Api.Application;
using Celd.Api.Auth;
using Celd.Api.Data;
using Celd.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Celd.Tests.Support;

public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public sealed class FakeCurrentUser : ICurrentUserAccessor
{
    public CurrentUser? User { get; set; }

    public CurrentUser? Get() => User;
}

/// <summary>
/// Profile B service-unit harness. Uses SQLite in memory: a real relational engine, so the filtered unique
/// index, foreign keys and the Version concurrency token are enforced. It is NOT a stand-in for PostgreSQL
/// concurrency; AC-04 and AC-10 are proven in the Integration folder against real PostgreSQL.
/// Each call gets a fresh DbContext, like a separate HTTP request.
/// </summary>
public sealed class ServiceHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public ServiceHarness()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var db = NewContext();
        db.Database.EnsureCreated();

        Student1 = AddUser(db, "student1", "Student One", Role.Student);
        Student2 = AddUser(db, "student2", "Student Two", Role.Student);
        Staff = AddUser(db, "staff1", "Desk Staff", Role.Staff);
        ItemA = AddItem(db, "CAM-001", "Mirrorless Camera Kit", "Camera");
        ItemB = AddItem(db, "AUD-001", "Wireless Lavalier Microphone", "Audio");
        db.SaveChanges();
    }

    public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero));

    public FakeCurrentUser Current { get; } = new();

    public User Student1 { get; }

    public User Student2 { get; }

    public User Staff { get; }

    public EquipmentItem ItemA { get; }

    public EquipmentItem ItemB { get; }

    public CeldDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CeldDbContext>().UseSqlite(_connection).Options);

    /// <summary>Runs one "request" as the given user (null = Visitor).</summary>
    public async Task<T> As<T>(User? user, Func<CentralApplicationService, Task<T>> call)
    {
        Current.User = user is null ? null : new CurrentUser(user.Id, user.Role, user.DisplayName);
        Clock.Advance(TimeSpan.FromMinutes(1));

        await using var db = NewContext();
        var app = new CentralApplicationService(
            Current,
            new EquipmentService(db),
            new ReservationService(db, new HistoryOperations(db), Clock));
        return await call(app);
    }

    public async Task<Reservation> LoadAsync(Guid reservationId)
    {
        await using var db = NewContext();
        return await db.Reservations.AsNoTracking().Include(r => r.History).SingleAsync(r => r.Id == reservationId);
    }

    public async Task<int> CountAsync<TEntity>()
        where TEntity : class
    {
        await using var db = NewContext();
        return await db.Set<TEntity>().CountAsync();
    }

    public void Dispose() => _connection.Dispose();

    private static User AddUser(CeldDbContext db, string userName, string displayName, Role role)
    {
        var user = new User { Id = Guid.NewGuid(), UserName = userName, DisplayName = displayName, Role = role, PasswordHash = "not-used-in-unit-tests" };
        db.Users.Add(user);
        return user;
    }

    private static EquipmentItem AddItem(CeldDbContext db, string code, string name, string category)
    {
        var item = new EquipmentItem { Id = Guid.NewGuid(), AssetCode = code, Name = name, Category = category, Description = name };
        db.EquipmentItems.Add(item);
        return item;
    }
}
