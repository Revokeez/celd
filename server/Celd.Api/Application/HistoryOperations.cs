using Celd.Api.Data;
using Celd.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Celd.Api.Application;

/// <summary>
/// Builds and reads History Events (DR-004, FR-RES-007).
/// Rule: this class NEVER calls SaveChanges. It adds the event to the caller's DbContext, so the event
/// commits in the same transaction as the reservation change it describes (BR-RES-004).
/// </summary>
public sealed class HistoryOperations(CeldDbContext db)
{
    public void Record(Reservation reservation, string eventType, Guid actorId, DateTime occurredAtUtc)
    {
        db.HistoryEvents.Add(new HistoryEvent
        {
            Id = Guid.NewGuid(),
            ReservationId = reservation.Id,
            EventType = eventType,
            ResultingStatus = reservation.Status,
            ActorId = actorId,
            OccurredAtUtc = occurredAtUtc,
        });
    }

    public async Task<IReadOnlyList<HistoryEventDto>> ListAsync(Guid reservationId, CancellationToken ct) =>
        await db.HistoryEvents.AsNoTracking()
            .Where(h => h.ReservationId == reservationId)
            .OrderBy(h => h.OccurredAtUtc)
            .Select(h => new HistoryEventDto(h.EventType, h.ResultingStatus.ToString(), h.ActorId, h.Actor!.DisplayName, h.OccurredAtUtc))
            .ToListAsync(ct);
}
