using Celd.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Celd.Api.Data;

public sealed class CeldDbContext(DbContextOptions<CeldDbContext> options) : DbContext(options)
{
    /// <summary>
    /// BR-RES-002. The database, not application code, guarantees at most one active reservation per item.
    /// The filter uses double-quoted identifiers, which both PostgreSQL and SQLite accept.
    /// </summary>
    public const string OneActivePerItemIndex = "IX_Reservations_OneActivePerItem";

    public const string OneActivePerItemFilter = "\"Status\" IN ('Reserved', 'CheckedOut')";

    public DbSet<User> Users => Set<User>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<EquipmentItem> EquipmentItems => Set<EquipmentItem>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<HistoryEvent> HistoryEvents => Set<HistoryEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Every DateTime is UTC (DR-004). Reads are tagged Utc so JSON carries a trailing "Z".
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.HasIndex(u => u.UserName).IsUnique();
            e.Property(u => u.UserName).HasMaxLength(64);
            e.Property(u => u.DisplayName).HasMaxLength(100);
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        });

        model.Entity<UserSession>(e =>
        {
            e.HasIndex(s => s.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EquipmentItem>(e =>
        {
            e.HasIndex(i => i.AssetCode).IsUnique();
            e.Property(i => i.AssetCode).HasMaxLength(32);
            e.Property(i => i.Name).HasMaxLength(120);
            e.Property(i => i.Category).HasMaxLength(60);
            e.Property(i => i.Description).HasMaxLength(1000);
        });

        model.Entity<Reservation>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Version).IsConcurrencyToken();

            e.HasIndex(r => r.EquipmentItemId)
                .IsUnique()
                .HasFilter(OneActivePerItemFilter)
                .HasDatabaseName(OneActivePerItemIndex);
            e.HasIndex(r => r.StudentId);
            e.HasIndex(r => r.Status);

            // Restrict everywhere: nothing cascades away reservations or history (BR-RES-008).
            e.HasOne(r => r.EquipmentItem).WithMany().HasForeignKey(r => r.EquipmentItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Student).WithMany().HasForeignKey(r => r.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.History).WithOne().HasForeignKey(h => h.ReservationId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HistoryEvent>(e =>
        {
            e.Property(h => h.EventType).HasMaxLength(20);
            e.Property(h => h.ResultingStatus).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(h => h.ReservationId);
            e.HasOne(h => h.Actor).WithMany().HasForeignKey(h => h.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
