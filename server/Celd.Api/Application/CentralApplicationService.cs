using Celd.Api.Auth;
using Celd.Api.Domain;

namespace Celd.Api.Application;

/// <summary>
/// The single entry point controllers call (Profile B). It resolves the caller from the session,
/// rechecks the caller's role, and delegates. It holds no business rules of its own; if a rule
/// appears here, it belongs in EquipmentService or ReservationService instead.
/// </summary>
public sealed class CentralApplicationService(
    ICurrentUserAccessor currentUser,
    EquipmentService equipment,
    ReservationService reservations)
{
    // UC-001: public, no session required.
    public Task<IReadOnlyList<EquipmentSummaryDto>> ListEquipmentAsync(string? search, string? category, CancellationToken ct) =>
        equipment.ListAsync(search, category, ct);

    public Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct) => equipment.ListCategoriesAsync(ct);

    public async Task<OperationResult<EquipmentDetailDto>> GetEquipmentAsync(Guid id, CancellationToken ct)
    {
        var item = await equipment.GetAsync(id, ct);
        return item is null
            ? OperationError.NotFound("equipment_not_found", "That equipment item does not exist.")
            : OperationResult<EquipmentDetailDto>.Ok(item);
    }

    // UC-002
    public async Task<OperationResult<ReservationDto>> ReserveAsync(Guid itemId, CancellationToken ct) =>
        Require(Role.Student, out var user) is { } error ? error : await reservations.CreateAsync(user, itemId, ct);

    // UC-003
    public async Task<OperationResult<IReadOnlyList<ReservationDto>>> ListMyReservationsAsync(CancellationToken ct) =>
        Require(Role.Student, out var user) is { } error
            ? error
            : OperationResult<IReadOnlyList<ReservationDto>>.Ok(await reservations.ListForStudentAsync(user, ct));

    public async Task<OperationResult<ReservationDto>> CancelAsync(Guid reservationId, CancellationToken ct) =>
        Require(Role.Student, out var user) is { } error
            ? error
            : await reservations.TransitionAsync(user, reservationId, LifecycleAction.Cancel, ct);

    // UC-003 / UC-004 / UC-005 shared reads: Students see their own, Staff see all.
    public async Task<OperationResult<ReservationDto>> GetReservationAsync(Guid reservationId, CancellationToken ct) =>
        RequireSignedIn(out var user) is { } error ? error : await reservations.GetAsync(user, reservationId, ct);

    public async Task<OperationResult<IReadOnlyList<HistoryEventDto>>> GetHistoryAsync(Guid reservationId, CancellationToken ct) =>
        RequireSignedIn(out var user) is { } error ? error : await reservations.GetHistoryAsync(user, reservationId, ct);

    // UC-004 / UC-005
    public async Task<OperationResult<IReadOnlyList<ReservationDto>>> ListStaffQueueAsync(ReservationStatus? status, CancellationToken ct) =>
        Require(Role.Staff, out _) is { } error
            ? error
            : OperationResult<IReadOnlyList<ReservationDto>>.Ok(await reservations.ListForStaffAsync(status, ct));

    public async Task<OperationResult<ReservationDto>> RecordPickupAsync(Guid reservationId, CancellationToken ct) =>
        Require(Role.Staff, out var user) is { } error
            ? error
            : await reservations.TransitionAsync(user, reservationId, LifecycleAction.RecordPickup, ct);

    public async Task<OperationResult<ReservationDto>> RecordReturnAsync(Guid reservationId, CancellationToken ct) =>
        Require(Role.Staff, out var user) is { } error
            ? error
            : await reservations.TransitionAsync(user, reservationId, LifecycleAction.RecordReturn, ct);

    /// <summary>
    /// Defence in depth: the controllers' [Authorize(Roles=...)] attributes already block wrong roles,
    /// but this recheck means calling the service from anywhere else is still safe (BR-AUT-003).
    /// </summary>
    private OperationError? Require(Role role, out CurrentUser user)
    {
        if (RequireSignedIn(out user) is { } error)
        {
            return error;
        }

        return user.Role == role ? null : OperationError.Forbidden();
    }

    private OperationError? RequireSignedIn(out CurrentUser user)
    {
        var resolved = currentUser.Get();
        user = resolved!;
        return resolved is null ? OperationError.Unauthenticated() : null;
    }
}
