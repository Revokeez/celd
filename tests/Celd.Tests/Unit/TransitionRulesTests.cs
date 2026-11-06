using Celd.Api.Application;
using Celd.Api.Domain;

namespace Celd.Tests.Unit;

/// <summary>UT-RES-06 — BR-RES-003. Every (status, action, role) combination: exactly three are legal.</summary>
public sealed class TransitionRulesTests
{
    public static TheoryData<ReservationStatus, LifecycleAction, Role> AllCombinations()
    {
        var data = new TheoryData<ReservationStatus, LifecycleAction, Role>();
        foreach (var status in Enum.GetValues<ReservationStatus>())
        foreach (var action in Enum.GetValues<LifecycleAction>())
        foreach (var role in Enum.GetValues<Role>())
        {
            data.Add(status, action, role);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Only_the_three_SRS_transitions_are_allowed(ReservationStatus current, LifecycleAction action, Role role)
    {
        var expected = (current, action, role) switch
        {
            (ReservationStatus.Reserved, LifecycleAction.RecordPickup, Role.Staff) => ReservationStatus.CheckedOut,
            (ReservationStatus.Reserved, LifecycleAction.Cancel, Role.Student) => ReservationStatus.Cancelled,
            (ReservationStatus.CheckedOut, LifecycleAction.RecordReturn, Role.Staff) => ReservationStatus.Returned,
            _ => (ReservationStatus?)null,
        };

        var check = ReservationTransitions.Check(current, action, role);

        if (expected is null)
        {
            Assert.NotEqual(TransitionOutcome.Allowed, check.Outcome);
            Assert.Equal(current, check.To); // refusal never proposes a new status
        }
        else
        {
            Assert.Equal(TransitionOutcome.Allowed, check.Outcome);
            Assert.Equal(expected.Value, check.To);
        }
    }

    [Theory]
    [InlineData(LifecycleAction.RecordPickup)]
    [InlineData(LifecycleAction.RecordReturn)]
    public void Students_get_wrong_role_for_staff_actions_in_any_status(LifecycleAction action)
    {
        foreach (var status in Enum.GetValues<ReservationStatus>())
        {
            Assert.Equal(TransitionOutcome.WrongRole, ReservationTransitions.Check(status, action, Role.Student).Outcome);
        }
    }

    [Fact]
    public void Staff_cannot_cancel()
    {
        Assert.Equal(TransitionOutcome.WrongRole, ReservationTransitions.Check(ReservationStatus.Reserved, LifecycleAction.Cancel, Role.Staff).Outcome);
    }

    [Theory]
    [InlineData(ReservationStatus.Reserved, true)]
    [InlineData(ReservationStatus.CheckedOut, true)]
    [InlineData(ReservationStatus.Cancelled, false)]
    [InlineData(ReservationStatus.Returned, false)]
    public void Active_means_Reserved_or_CheckedOut(ReservationStatus status, bool active)
    {
        Assert.Equal(active, ReservationTransitions.IsActive(status));
    }

    [Fact]
    public void Active_filter_in_the_database_index_matches_IsActive()
    {
        // Guards against someone adding a status to IsActive but forgetting the BR-RES-002 index filter.
        foreach (var status in Enum.GetValues<ReservationStatus>())
        {
            var inFilter = Celd.Api.Data.CeldDbContext.OneActivePerItemFilter.Contains($"'{status}'");
            Assert.Equal(ReservationTransitions.IsActive(status), inFilter);
        }
    }
}
