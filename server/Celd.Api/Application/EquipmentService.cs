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
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    public async Task<PagedResult<EquipmentSummaryDto>> ListAsync(
        string? search, string? category, bool availableOnly, int? page, int? pageSize, CancellationToken ct)
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

        if (availableOnly)
        {
            // Filtered before counting, so page totals describe only what the user asked to see.
            query = query.Where(i => !db.Reservations.Any(r => r.EquipmentItemId == i.Id
                && (r.Status == ReservationStatus.Reserved || r.Status == ReservationStatus.CheckedOut)));
        }

        // Out-of-range values are clamped rather than rejected, so a stale link still lands on a real page.
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var total = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
        var current = Math.Clamp(page ?? 1, 1, totalPages);

        var items = await query
            // AssetCode is unique, so the order is total and no item repeats or goes missing across pages.
            .OrderBy(i => i.Name).ThenBy(i => i.AssetCode)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(i => new EquipmentSummaryDto(
                i.Id,
                i.AssetCode,
                i.Name,
                i.Category,
                !db.Reservations.Any(r => r.EquipmentItemId == i.Id
                    && (r.Status == ReservationStatus.Reserved || r.Status == ReservationStatus.CheckedOut))))
            .ToListAsync(ct);

        return new PagedResult<EquipmentSummaryDto>(items, current, size, total, totalPages);
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
