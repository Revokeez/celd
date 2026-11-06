using Celd.Api.Data;
using Celd.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Celd.Api.Application;

/// <summary>
/// Catalog queries (FR-CAT-001..003). Availability is computed from active reservations on every read,
/// so it cannot drift from the authoritative state (DR-006, BR-RES-007).
/// </summary>
public sealed class EquipmentService(CeldDbContext db)
{
    public async Task<IReadOnlyList<EquipmentSummaryDto>> ListAsync(string? search, string? category, CancellationToken ct)
    {
        var query = db.EquipmentItems.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive match (UC-001 A2.1), written portably rather than with a provider-specific ILIKE.
            var term = search.Trim().ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var wanted = category.Trim().ToLower();
            query = query.Where(i => i.Category.ToLower() == wanted);
        }

        return await query
            .OrderBy(i => i.Name)
            .Select(i => new EquipmentSummaryDto(
                i.Id,
                i.AssetCode,
                i.Name,
                i.Category,
                !db.Reservations.Any(r => r.EquipmentItemId == i.Id
                    && (r.Status == ReservationStatus.Reserved || r.Status == ReservationStatus.CheckedOut))))
            .ToListAsync(ct);
    }

    public async Task<EquipmentDetailDto?> GetAsync(Guid id, CancellationToken ct) =>
        await db.EquipmentItems.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new EquipmentDetailDto(
                i.Id,
                i.AssetCode,
                i.Name,
                i.Category,
                i.Description,
                !db.Reservations.Any(r => r.EquipmentItemId == i.Id
                    && (r.Status == ReservationStatus.Reserved || r.Status == ReservationStatus.CheckedOut))))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct) =>
        await db.EquipmentItems.AsNoTracking().Select(i => i.Category).Distinct().OrderBy(c => c).ToListAsync(ct);
}
