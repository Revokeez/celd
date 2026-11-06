using Celd.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Celd.Api.Data;

/// <summary>
/// Reproducible setup (SRS §2.3). Items are fixed demo data. Accounts come from configuration,
/// and their passwords must come from user-secrets or environment variables, never from committed files.
/// </summary>
public static class DbSeeder
{
    private static readonly (string Code, string Name, string Category, string Description)[] Items =
    [
        ("CAM-001", "Mirrorless Camera Kit", "Camera", "24 MP mirrorless body with 18-45 mm lens, battery and charger."),
        ("CAM-002", "Compact Vlogging Camera", "Camera", "Small camera with flip screen for video interviews."),
        ("AUD-001", "Wireless Lavalier Microphone", "Audio", "Two-transmitter wireless clip-on microphone set."),
        ("AUD-002", "Portable Field Recorder", "Audio", "Four-track handheld recorder with XY microphone capsule."),
        ("LAP-001", "13-inch Loaner Laptop", "Laptop", "Lightweight laptop with office and programming tools installed."),
        ("PRJ-001", "Portable Projector", "Projector", "1080p projector with HDMI and USB-C inputs."),
        ("ACC-001", "Aluminum Tripod", "Accessory", "Full-size tripod with quick-release plate."),
    ];

    public static async Task SeedAsync(
        CeldDbContext db, IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        if (!await db.EquipmentItems.AnyAsync(ct))
        {
            foreach (var (code, name, category, description) in Items)
            {
                db.EquipmentItems.Add(new EquipmentItem
                {
                    Id = Guid.NewGuid(), AssetCode = code, Name = name, Category = category, Description = description,
                });
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} equipment items.", Items.Length);
        }

        await SeedUsersAsync(db, config, logger, ct);

        if (config.GetValue<bool>("Seed:PerformanceData"))
        {
            await SeedPerformanceDataAsync(db, logger, ct);
        }
    }

    private static async Task SeedUsersAsync(CeldDbContext db, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var hasher = new PasswordHasher<User>();

        foreach (var section in config.GetSection("Seed:Users").GetChildren())
        {
            var userName = section["UserName"]?.Trim().ToLowerInvariant();
            var password = section["Password"];

            if (string.IsNullOrWhiteSpace(userName))
            {
                continue;
            }

            if (string.IsNullOrEmpty(password))
            {
                // Never fall back to a default password.
                logger.LogWarning("Seed user {UserName} has no password configured and was skipped. See README.", userName);
                continue;
            }

            if (!Enum.TryParse<Role>(section["Role"], ignoreCase: true, out var role))
            {
                logger.LogWarning("Seed user {UserName} has an unknown role and was skipped.", userName);
                continue;
            }

            if (await db.Users.AnyAsync(u => u.UserName == userName, ct))
            {
                continue;
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                DisplayName = section["DisplayName"] ?? userName,
                Role = role,
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            logger.LogInformation("Seeded {Role} account {UserName}.", role, userName);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>NFR-PERF-001: at least 50 items and 100 historical reservations.</summary>
    private static async Task SeedPerformanceDataAsync(CeldDbContext db, ILogger logger, CancellationToken ct)
    {
        var student = await db.Users.FirstOrDefaultAsync(u => u.Role == Role.Student, ct);
        var staff = await db.Users.FirstOrDefaultAsync(u => u.Role == Role.Staff, ct);
        if (student is null || staff is null)
        {
            logger.LogWarning("Performance seed skipped: it needs one Student and one Staff account.");
            return;
        }

        var itemCount = await db.EquipmentItems.CountAsync(ct);
        for (var n = itemCount + 1; n <= 50; n++)
        {
            db.EquipmentItems.Add(new EquipmentItem
            {
                Id = Guid.NewGuid(),
                AssetCode = $"PERF-{n:000}",
                Name = $"Performance Test Item {n:000}",
                Category = n % 2 == 0 ? "Accessory" : "Audio",
                Description = "Generated for the NFR-PERF-001 performance run.",
            });
        }

        await db.SaveChangesAsync(ct);

        var historical = await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Returned || r.Status == ReservationStatus.Cancelled, ct);
        var itemIds = await db.EquipmentItems.Select(i => i.Id).ToListAsync(ct);
        var start = DateTime.UtcNow.AddDays(-60);

        for (var n = historical; n < 100; n++)
        {
            var finalStatus = n % 3 == 0 ? ReservationStatus.Cancelled : ReservationStatus.Returned;
            var created = start.AddHours(n);
            var reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                EquipmentItemId = itemIds[n % itemIds.Count],
                StudentId = student.Id,
                Status = finalStatus,
                CreatedAtUtc = created,
                Version = 1,
            };
            reservation.History.Add(Event(reservation.Id, "Created", ReservationStatus.Reserved, student.Id, created));
            if (finalStatus == ReservationStatus.Cancelled)
            {
                reservation.History.Add(Event(reservation.Id, "Cancelled", ReservationStatus.Cancelled, student.Id, created.AddMinutes(30)));
            }
            else
            {
                reservation.History.Add(Event(reservation.Id, "PickedUp", ReservationStatus.CheckedOut, staff.Id, created.AddHours(2)));
                reservation.History.Add(Event(reservation.Id, "Returned", ReservationStatus.Returned, staff.Id, created.AddDays(1)));
            }
            db.Reservations.Add(reservation);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Performance seed ready: 50+ items, 100+ historical reservations.");
    }

    private static HistoryEvent Event(Guid reservationId, string type, ReservationStatus status, Guid actor, DateTime at) =>
        new() { Id = Guid.NewGuid(), ReservationId = reservationId, EventType = type, ResultingStatus = status, ActorId = actor, OccurredAtUtc = at };
}
