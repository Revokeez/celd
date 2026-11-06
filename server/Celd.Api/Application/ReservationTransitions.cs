using Celd.Api.Domain;

namespace Celd.Api.Application;

public enum LifecycleAction
{
    Cancel,
    RecordPickup,
    RecordReturn,
}

public enum TransitionOutcome
{
    Allowed,
    WrongRole,
    IllegalFromCurrentStatus,
}

public sealed record TransitionCheck(TransitionOutcome Outcome, ReservationStatus To);

/// <summary>
/// BR-RES-003: the complete list of legal transitions. Anything not in this table is refused.
/// Pure and side-effect free, so it is tested exhaustively without a database.
/// </summary>
public static class ReservationTransitions
{
    private static readonly (ReservationStatus From, LifecycleAction Action, Role Actor, ReservationStatus To, string EventType)[] Legal =
    [
        (ReservationStatus.Reserved, LifecycleAction.RecordPickup, Role.Staff, ReservationStatus.CheckedOut, "PickedUp"),
        (ReservationStatus.Reserved, LifecycleAction.Cancel, Role.Student, ReservationStatus.Cancelled, "Cancelled"),
        (ReservationStatus.CheckedOut, LifecycleAction.RecordReturn, Role.Staff, ReservationStatus.Returned, "Returned"),
    ];

    public static TransitionCheck Check(ReservationStatus current, LifecycleAction action, Role actor)
    {
        // Role is checked against the action first, so a Student asking for pickup is told "forbidden"
        // whatever the reservation's state.
        var rulesForAction = Legal.Where(t => t.Action == action).ToArray();
        if (rulesForAction.All(t => t.Actor != actor))
        {
            return new TransitionCheck(TransitionOutcome.WrongRole, current);
        }

        foreach (var rule in rulesForAction)
        {
            if (rule.From == current && rule.Actor == actor)
            {
                return new TransitionCheck(TransitionOutcome.Allowed, rule.To);
            }
        }

        return new TransitionCheck(TransitionOutcome.IllegalFromCurrentStatus, current);
    }

    public static string EventTypeFor(LifecycleAction action) =>
        Legal.First(t => t.Action == action).EventType;

    /// <summary>An Active Reservation is Reserved or CheckedOut (SRS §1.4).</summary>
    public static bool IsActive(ReservationStatus status) =>
        status is ReservationStatus.Reserved or ReservationStatus.CheckedOut;
}
