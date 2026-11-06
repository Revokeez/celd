using Celd.Api.Application;
using Celd.Api.Domain;
using Celd.Tests.Support;

namespace Celd.Tests.Unit;

/// <summary>
/// Profile B emphasis: service-unit tests of the Central Application Service and Reservation Service,
/// on an in-memory relational engine. Test IDs match the Week 1 RTM.
/// </summary>
public sealed class CentralApplicationServiceTests : IDisposable
{
    private readonly ServiceHarness _h = new();

    public void Dispose() => _h.Dispose();

    // ---------------- UC-002 Reserve ----------------

    [Fact] // UT-RES-01 — FR-RES-001, BR-RES-004
    public async Task Reserve_available_item_creates_Reserved_reservation_with_creation_history()
    {
        var result = await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemA.Id, default));

        Assert.True(result.Succeeded);
        Assert.Equal("Reserved", result.Value!.Status);
        Assert.Equal(_h.Student1.Id, result.Value.StudentId);

        var saved = await _h.LoadAsync(result.Value.Id);
        var created = Assert.Single(saved.History);
        Assert.Equal("Created", created.EventType);
        Assert.Equal(ReservationStatus.Reserved, created.ResultingStatus);
        Assert.Equal(_h.Student1.Id, created.ActorId);
        Assert.Equal(DateTimeKind.Utc, created.OccurredAtUtc.Kind);
    }

    [Fact] // UT-RES-05 — BR-RES-001, BR-RES-006
    public async Task Reserve_unavailable_item_is_conflict_and_writes_nothing()
    {
        await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemA.Id, default));
        var reservationsBefore = await _h.CountAsync<Reservation>();
        var historyBefore = await _h.CountAsync<HistoryEvent>();

        var second = await _h.As(_h.Student2, app => app.ReserveAsync(_h.ItemA.Id, default));

        Assert.Equal(ErrorKind.Conflict, second.Error!.Kind);
        Assert.Equal("item_unavailable", second.Error.Code);
        Assert.Equal(reservationsBefore, await _h.CountAsync<Reservation>());
        Assert.Equal(historyBefore, await _h.CountAsync<HistoryEvent>());
    }

    [Fact] // UC-002 E5.1
    public async Task Reserve_missing_item_is_not_found()
    {
        var result = await _h.As(_h.Student1, app => app.ReserveAsync(Guid.NewGuid(), default));
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact] // UC-002 E4.1 / E4.2 — BR-AUT-001
    public async Task Reserve_requires_a_signed_in_student()
    {
        var visitor = await _h.As(null, app => app.ReserveAsync(_h.ItemA.Id, default));
        var staff = await _h.As(_h.Staff, app => app.ReserveAsync(_h.ItemA.Id, default));

        Assert.Equal(ErrorKind.Unauthenticated, visitor.Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, staff.Error!.Kind);
        Assert.Equal(0, await _h.CountAsync<Reservation>());
    }

    // ---------------- UC-003 View and cancel ----------------

    [Fact] // UT-RES-02 — FR-RES-003, BR-RES-005
    public async Task Owner_cancels_Reserved_reservation_and_item_becomes_available()
    {
        var created = (await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemA.Id, default))).Value!;

        var cancelled = await _h.As(_h.Student1, app => app.CancelAsync(created.Id, default));

        Assert.Equal("Cancelled", cancelled.Value!.Status);
        Assert.False(cancelled.Value.IsActive);
        var saved = await _h.LoadAsync(created.Id);
        Assert.Equal(new[] { "Created", "Cancelled" }, saved.History.OrderBy(e => e.OccurredAtUtc).Select(e => e.EventType));

        var item = await _h.As(null, app => app.GetEquipmentAsync(_h.ItemA.Id, default));
        Assert.True(item.Value!.IsAvailable);
    }

    [Fact] // UT-AUT-01 — BR-AUT-002, AC-05
    public async Task Student_cannot_see_or_cancel_another_students_reservation()
    {
        var created = (await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemA.Id, default))).Value!;

        var read = await _h.As(_h.Student2, app => app.GetReservationAsync(created.Id, default));
        var history = await _h.As(_h.Student2, app => app.GetHistoryAsync(created.Id, default));
        var cancel = await _h.As(_h.Student2, app => app.CancelAsync(created.Id, default));
        var mine = await _h.As(_h.Student2, app => app.ListMyReservationsAsync(default));

        // Same answer as "does not exist", so nothing about the record is revealed (SRS question Q2).
        Assert.Equal(ErrorKind.NotFound, read.Error!.Kind);
        Assert.Equal(ErrorKind.NotFound, history.Error!.Kind);
        Assert.Equal(ErrorKind.NotFound, cancel.Error!.Kind);
        Assert.Empty(mine.Value!);

        var saved = await _h.LoadAsync(created.Id);
        Assert.Equal(ReservationStatus.Reserved, saved.Status);
        Assert.Single(saved.History);
    }

    [Fact] // FR-RES-002
    public async Task My_reservations_lists_active_and_finished_records_distinguishably()
    {
        var first = (await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemA.Id, default))).Value!;
        await _h.As(_h.Student1, app => app.CancelAsync(first.Id, default));
        await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemB.Id, default));

        var mine = (await _h.As(_h.Student1, app => app.ListMyReservationsAsync(default))).Value!;

        Assert.Equal(2, mine.Count);
        Assert.Single(mine, r => r.IsActive && r.Status == "Reserved");
        Assert.Single(mine, r => !r.IsActive && r.Status == "Cancelled");
    }

    // ---------------- UC-004 / UC-005 Pickup and return ----------------

    [Fact] // UT-RES-03 + UT-RES-04 — FR-RES-005, FR-RES-006, BR-RES-005, AC-08
    public async Task Staff_records_pickup_then_return_and_item_can_be_reserved_again()
    {
        var created = (await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemA.Id, default))).Value!;

        var pickedUp = await _h.As(_h.Staff, app => app.RecordPickupAsync(created.Id, default));
        Assert.Equal("CheckedOut", pickedUp.Value!.Status);
        Assert.False((await _h.As(null, app => app.GetEquipmentAsync(_h.ItemA.Id, default))).Value!.IsAvailable);

        var returned = await _h.As(_h.Staff, app => app.RecordReturnAsync(created.Id, default));
        Assert.Equal("Returned", returned.Value!.Status);

        var again = await _h.As(_h.Student2, app => app.ReserveAsync(_h.ItemA.Id, default));
        Assert.True(again.Succeeded);

        var history = (await _h.LoadAsync(created.Id)).History.OrderBy(e => e.OccurredAtUtc).ToList();
        Assert.Equal(new[] { "Created", "PickedUp", "Returned" }, history.Select(e => e.EventType));
        Assert.Equal(new[] { _h.Student1.Id, _h.Staff.Id, _h.Staff.Id }, history.Select(e => e.ActorId));
    }

    [Fact] // UT-RES-07 — BR-RES-006, AC-09
    public async Task Repeated_and_out_of_order_transitions_are_refused_without_history()
    {
        var created = (await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemA.Id, default))).Value!;
        await _h.As(_h.Staff, app => app.RecordPickupAsync(created.Id, default));
        await _h.As(_h.Staff, app => app.RecordReturnAsync(created.Id, default));
        var historyBefore = (await _h.LoadAsync(created.Id)).History.Count;

        var secondReturn = await _h.As(_h.Staff, app => app.RecordReturnAsync(created.Id, default));
        var pickupAfterReturn = await _h.As(_h.Staff, app => app.RecordPickupAsync(created.Id, default));
        var cancelAfterReturn = await _h.As(_h.Student1, app => app.CancelAsync(created.Id, default));

        Assert.All(new[] { secondReturn, pickupAfterReturn, cancelAfterReturn }, r =>
        {
            Assert.Equal(ErrorKind.Conflict, r.Error!.Kind);
            Assert.Equal("invalid_transition", r.Error.Code);
        });

        var saved = await _h.LoadAsync(created.Id);
        Assert.Equal(ReservationStatus.Returned, saved.Status);
        Assert.Equal(historyBefore, saved.History.Count);
    }

    [Fact] // BR-RES-003 — Cancel is not allowed after pickup
    public async Task Cancel_after_pickup_is_refused()
    {
        var created = (await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemA.Id, default))).Value!;
        await _h.As(_h.Staff, app => app.RecordPickupAsync(created.Id, default));

        var cancel = await _h.As(_h.Student1, app => app.CancelAsync(created.Id, default));

        Assert.Equal("invalid_transition", cancel.Error!.Code);
        Assert.Equal(ReservationStatus.CheckedOut, (await _h.LoadAsync(created.Id)).Status);
    }

    [Fact] // BR-AUT-003 — service recheck, independent of controller attributes
    public async Task Students_and_visitors_cannot_record_pickup_or_return_through_the_service()
    {
        var created = (await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemA.Id, default))).Value!;

        Assert.Equal(ErrorKind.Forbidden, (await _h.As(_h.Student1, app => app.RecordPickupAsync(created.Id, default))).Error!.Kind);
        Assert.Equal(ErrorKind.Unauthenticated, (await _h.As(null, app => app.RecordPickupAsync(created.Id, default))).Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, (await _h.As(_h.Student1, app => app.RecordReturnAsync(created.Id, default))).Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, (await _h.As(_h.Staff, app => app.CancelAsync(created.Id, default))).Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, (await _h.As(_h.Student1, app => app.ListStaffQueueAsync(null, default))).Error!.Kind);
    }

    [Fact] // FR-RES-004
    public async Task Staff_queue_filters_by_status()
    {
        var a = (await _h.As(_h.Student1, app => app.ReserveAsync(_h.ItemA.Id, default))).Value!;
        await _h.As(_h.Student2, app => app.ReserveAsync(_h.ItemB.Id, default));
        await _h.As(_h.Staff, app => app.RecordPickupAsync(a.Id, default));

        var reserved = (await _h.As(_h.Staff, app => app.ListStaffQueueAsync(ReservationStatus.Reserved, default))).Value!;
        var all = (await _h.As(_h.Staff, app => app.ListStaffQueueAsync(null, default))).Value!;

        Assert.All(reserved, r => Assert.Equal("Reserved", r.Status));
        Assert.Single(reserved);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, r => r.StudentName == "Student One" && r.ItemName == "Mirrorless Camera Kit");
    }

    // ---------------- UC-001 Catalog ----------------

    [Fact] // FR-CAT-002
    public async Task Catalog_search_is_case_insensitive_and_combines_with_category()
    {
        var byName = await _h.As(null, app => app.ListEquipmentAsync("CAMERA", null, default));
        var combined = await _h.As(null, app => app.ListEquipmentAsync("kit", "camera", default));
        var none = await _h.As(null, app => app.ListEquipmentAsync("kit", "audio", default));

        Assert.Equal("CAM-001", Assert.Single(byName).AssetCode);
        Assert.Equal("CAM-001", Assert.Single(combined).AssetCode);
        Assert.Empty(none);
    }
}
