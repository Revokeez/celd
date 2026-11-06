namespace Celd.Api.Domain;

public enum Role
{
    Student,
    Staff,
}

public enum ReservationStatus
{
    Reserved,
    CheckedOut,
    Cancelled,
    Returned,
}

/// <summary>A provisioned account (DR-002). Created only by seed/setup, never through the UI.</summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Stored lower-case so sign-in is case-insensitive on the user name only.</summary>
    public string UserName { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>ASP.NET Core PasswordHasher output. Never plaintext (DR-002, NFR-SEC-002).</summary>
    public string PasswordHash { get; set; } = "";

    public Role Role { get; set; }
}

/// <summary>
/// Server-side record of a signed-in session. Sign-out revokes it, so a copied cookie stops
/// working after sign-out (FR-AUT-002).
/// </summary>
public class UserSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
}

/// <summary>One physically distinguishable item (DR-001). Availability is computed, not stored (DR-006).</summary>
public class EquipmentItem
{
    public Guid Id { get; set; }

    public string AssetCode { get; set; } = "";

    public string Name { get; set; } = "";

    public string Category { get; set; } = "";

    public string Description { get; set; } = "";
}

/// <summary>DR-003. Status changes go only through ReservationService.</summary>
public class Reservation
{
    public Guid Id { get; set; }

    public Guid EquipmentItemId { get; set; }

    public EquipmentItem? EquipmentItem { get; set; }

    public Guid StudentId { get; set; }

    public User? Student { get; set; }

    public ReservationStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Optimistic concurrency token. Rejects stale transitions such as a cancel and a pickup racing.</summary>
    public int Version { get; set; }

    public List<HistoryEvent> History { get; set; } = [];
}

/// <summary>DR-004. Written in the same SaveChanges call as the change it records (BR-RES-004).</summary>
public class HistoryEvent
{
    public Guid Id { get; set; }

    public Guid ReservationId { get; set; }

    public string EventType { get; set; } = "";

    public ReservationStatus ResultingStatus { get; set; }

    public Guid ActorId { get; set; }

    public User? Actor { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}
