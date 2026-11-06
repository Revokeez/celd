using Celd.Api.Auth;
using Celd.Api.Data;
using Celd.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Celd.Api.Application;

/// <summary>
/// Owns every reservation rule: creation, ownership (BR-AUT-002), the transition table (BR-RES-003),
/// stale-update rejection, and saving each change together with its History Event (BR-RES-004).
/// This is the ONLY class that calls SaveChanges for reservations.
/// </summary>
public sealed class ReservationService(CeldDbContext db, HistoryOperations history, TimeProvider clock)
{
    public async Task<OperationResult<ReservationDto>> CreateAsync(CurrentUser student, Guid itemId, CancellationToken ct)
    {
        if (!await db.EquipmentItems.AnyAsync(i => i.Id == itemId, ct))
        {
            return OperationError.NotFound("equipment_not_found", "That equipment item does not exist.");
        }

        // Fast, friendly check. It is NOT the guarantee: two requests can both pass it at the same moment.
        // The filtered unique index below is what enforces BR-RES-002.
        var alreadyActive = await db.Reservations.AnyAsync(
            r => r.EquipmentItemId == itemId && (r.Status == ReservationStatus.Reserved || r.Status == ReservationStatus.CheckedOut), ct);
        if (alreadyActive)
        {
            return ItemUnavailable();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            EquipmentItemId = itemId,
            StudentId = student.Id,
            Status = ReservationStatus.Reserved,
            CreatedAtUtc = now,
            Version = 1,
        };
        db.Reservations.Add(reservation);
        history.Record(reservation, "Created", student.Id, now);

        try
        {
            // One SaveChanges = one transaction: the reservation and its history commit together or not at all.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsActiveReservationConflict(ex))
        {
            db.ChangeTracker.Clear();
            return ItemUnavailable();
        }

        return OperationResult<ReservationDto>.Ok((await ProjectAsync(r => r.Id == reservation.Id, ct)).Single());
    }

    public async Task<IReadOnlyList<ReservationDto>> ListForStudentAsync(CurrentUser student, CancellationToken ct) =>
        await ProjectAsync(r => r.StudentId == student.Id, ct);

    public async Task<IReadOnlyList<ReservationDto>> ListForStaffAsync(ReservationStatus? status, CancellationToken ct) =>
        status is null
            ? await ProjectAsync(_ => true, ct)
            : await ProjectAsync(r => r.Status == status, ct);

    public async Task<OperationResult<ReservationDto>> GetAsync(CurrentUser user, Guid reservationId, CancellationToken ct)
    {
        var owner = await FindOwnerAsync(reservationId, ct);
        if (owner is null || !CanView(user, owner.Value))
        {
            return ReservationNotFound();
        }

        return OperationResult<ReservationDto>.Ok((await ProjectAsync(r => r.Id == reservationId, ct)).Single());
    }

    public async Task<OperationResult<IReadOnlyList<HistoryEventDto>>> GetHistoryAsync(CurrentUser user, Guid reservationId, CancellationToken ct)
    {
        var owner = await FindOwnerAsync(reservationId, ct);
        if (owner is null || !CanView(user, owner.Value))
        {
            return ReservationNotFound();
        }

        return OperationResult<IReadOnlyList<HistoryEventDto>>.Ok(await history.ListAsync(reservationId, ct));
    }

    public async Task<OperationResult<ReservationDto>> TransitionAsync(
        CurrentUser actor, Guid reservationId, LifecycleAction action, CancellationToken ct)
    {
        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct);

        // A Student touching someone else's reservation gets the same answer as a missing one,
        // so the response reveals nothing about it (BR-AUT-002, NFR-SEC-001, SRS question Q2).
        if (reservation is null || !CanView(actor, reservation.StudentId))
        {
            return ReservationNotFound();
        }

        var check = ReservationTransitions.Check(reservation.Status, action, actor.Role);
        switch (check.Outcome)
        {
            case TransitionOutcome.WrongRole:
                return OperationError.Forbidden();
            case TransitionOutcome.IllegalFromCurrentStatus:
                // Repeated or stale requests land here and change nothing (BR-RES-006).
                return OperationError.Conflict(
                    "invalid_transition",
                    $"This reservation is {reservation.Status}, so that action is no longer possible. Refresh to see its current state.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        reservation.Status = check.To;
        reservation.Version++;
        history.Record(reservation, ReservationTransitions.EventTypeFor(action), actor.Id, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else changed this reservation after we read it. Their change wins; ours is discarded.
            db.ChangeTracker.Clear();
            return OperationError.Conflict(
                "stale_state",
                "This reservation was changed by someone else just now. Refresh to see its current state.");
        }

        return OperationResult<ReservationDto>.Ok((await ProjectAsync(r => r.Id == reservationId, ct)).Single());
    }

    private static bool CanView(CurrentUser user, Guid ownerId) =>
        user.Role == Role.Staff || ownerId == user.Id;

    private async Task<Guid?> FindOwnerAsync(Guid reservationId, CancellationToken ct) =>
        await db.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => (Guid?)r.StudentId)
            .FirstOrDefaultAsync(ct);

    private async Task<IReadOnlyList<ReservationDto>> ProjectAsync(
        System.Linq.Expressions.Expression<Func<Reservation, bool>> filter, CancellationToken ct)
    {
        var rows = await db.Reservations.AsNoTracking()
            .Where(filter)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.EquipmentItemId,
                ItemName = r.EquipmentItem!.Name,
                AssetCode = r.EquipmentItem!.AssetCode,
                r.StudentId,
                StudentName = r.Student!.DisplayName,
                r.Status,
                r.CreatedAtUtc,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ReservationDto(
                r.Id, r.EquipmentItemId, r.ItemName, r.AssetCode, r.StudentId, r.StudentName,
                r.Status.ToString(), ReservationTransitions.IsActive(r.Status), r.CreatedAtUtc))
            .ToList();
    }

    private static OperationError ItemUnavailable() =>
        OperationError.Conflict("item_unavailable", "This item is no longer available. Refresh the catalog to see its current status.");

    private static OperationError ReservationNotFound() =>
        OperationError.NotFound("reservation_not_found", "Reservation not found.");
}
