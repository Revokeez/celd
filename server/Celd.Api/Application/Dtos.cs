using System.ComponentModel.DataAnnotations;

namespace Celd.Api.Application;

// Public API shapes. Entities are never returned directly, so password hashes and session ids cannot leak.

public sealed record EquipmentSummaryDto(Guid Id, string AssetCode, string Name, string Category, bool IsAvailable);

/// <summary>One page of a list. Page and PageSize are the values actually applied, after clamping.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record EquipmentDetailDto(Guid Id, string AssetCode, string Name, string Category, string Description, bool IsAvailable);

public sealed record ReservationDto(
    Guid Id,
    Guid EquipmentItemId,
    string ItemName,
    string AssetCode,
    Guid StudentId,
    string StudentName,
    string Status,
    bool IsActive,
    DateTime CreatedAtUtc);

public sealed record HistoryEventDto(string EventType, string ResultingStatus, Guid ActorId, string ActorName, DateTime OccurredAtUtc);

public sealed record CurrentUserDto(Guid Id, string DisplayName, string Role);

public sealed record CsrfTokenDto(string Token);

// Validation attributes go on the constructor parameters, not with `property:`. MVC rejects the latter for records.
public sealed record LoginRequest(
    [Required, StringLength(64, MinimumLength = 1)] string UserName,
    [Required, StringLength(200, MinimumLength = 1)] string Password);

/// <summary>Only the item is accepted. Any student or role field a caller adds is ignored (BR-AUT-001).</summary>
public sealed record CreateReservationRequest([Required] Guid? EquipmentItemId);
