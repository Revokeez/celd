using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Celd.Api.Data;

public static class DbErrors
{
    /// <summary>
    /// True when the save lost the race for an item: the database rejected a second active
    /// reservation through the filtered unique index (BR-RES-002, NFR-REL-001).
    /// </summary>
    public static bool IsActiveReservationConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: CeldDbContext.OneActivePerItemIndex,
        };
}
