using System.Net;
using System.Net.Http.Json;
using Celd.Api.Application;
using Celd.Api.Domain;
using Celd.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Celd.Tests.Integration;

/// <summary>
/// AC-04, BR-RES-002, NFR-REL-001 — proven against real PostgreSQL, not an in-memory fake.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConcurrencyTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact] // IT-CON-01a: many simultaneous requests from two students
    public async Task Parallel_reserve_requests_yield_exactly_one_active_reservation()
    {
        var itemId = await ItemIdAsync("CAM-001");
        var student1 = await SignedInAsync(TestUsers.Student1);
        var student2 = await SignedInAsync(TestUsers.Student2);

        var requests = Enumerable.Range(0, 20)
            .Select(i => (i % 2 == 0 ? student1 : student2).PostAsync("/api/reservations", new { equipmentItemId = itemId }));
        var responses = await Task.WhenAll(requests);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            Assert.Equal("item_unavailable", await AssertProblemAsync(loser, HttpStatusCode.Conflict, "Conflict"));
        }

        await using var db = NewDb();
        Assert.Equal(1, await db.Reservations.CountAsync(r => r.EquipmentItemId == itemId));
        Assert.Equal(1, await db.HistoryEvents.CountAsync()); // losers left no history either
    }

    [Fact] // IT-CON-01b: deterministic race — a competitor commits between our availability check and our insert
    public async Task Database_index_rejects_a_competitor_that_slipped_past_the_availability_check()
    {
        var itemId = await ItemIdAsync("CAM-001");
        var student2Id = await UserIdAsync(TestUsers.Student2);
        var student1 = await SignedInAsync(TestUsers.Student1);

        Factory.Interceptor.Arm(
            when: ctx => ctx.ChangeTracker.Entries<Reservation>()
                .Any(e => e.State == EntityState.Added && e.Entity.EquipmentItemId == itemId),
            sql: $"""
                  INSERT INTO "Reservations" ("Id", "EquipmentItemId", "StudentId", "Status", "CreatedAtUtc", "Version")
                  VALUES ('{Guid.NewGuid()}', '{itemId}', '{student2Id}', 'Reserved', now(), 1)
                  """);

        var response = await student1.PostAsync("/api/reservations", new { equipmentItemId = itemId });

        Assert.True(Factory.Interceptor.Fired, "The race was not injected; the test proves nothing.");
        Assert.Equal("item_unavailable", await AssertProblemAsync(response, HttpStatusCode.Conflict, "Conflict"));

        await using var db = NewDb();
        var active = await db.Reservations.Where(r => r.EquipmentItemId == itemId).ToListAsync();
        Assert.Equal(student2Id, Assert.Single(active).StudentId);
        Assert.Equal(0, await db.HistoryEvents.CountAsync()); // the losing request's history rolled back with it
    }

    [Fact] // IT-CON-01c: the guarantee lives in the database even if application code is bypassed
    public async Task Database_refuses_a_second_active_reservation_inserted_directly()
    {
        var itemId = await ItemIdAsync("PRJ-001");
        var studentId = await UserIdAsync(TestUsers.Student1);

        await using var db = NewDb();
        db.Reservations.Add(new Reservation { Id = Guid.NewGuid(), EquipmentItemId = itemId, StudentId = studentId, Status = ReservationStatus.Reserved, CreatedAtUtc = DateTime.UtcNow, Version = 1 });
        await db.SaveChangesAsync();

        db.Reservations.Add(new Reservation { Id = Guid.NewGuid(), EquipmentItemId = itemId, StudentId = studentId, Status = ReservationStatus.CheckedOut, CreatedAtUtc = DateTime.UtcNow, Version = 1 });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(Celd.Api.Data.DbErrors.IsActiveReservationConflict(ex));
    }

    [Fact] // Finished reservations do not block the item (BR-RES-005)
    public async Task Cancelled_and_returned_rows_do_not_count_toward_the_index()
    {
        var itemId = await ItemIdAsync("PRJ-001");
        var studentId = await UserIdAsync(TestUsers.Student1);

        await using var db = NewDb();
        db.Reservations.Add(new Reservation { Id = Guid.NewGuid(), EquipmentItemId = itemId, StudentId = studentId, Status = ReservationStatus.Cancelled, CreatedAtUtc = DateTime.UtcNow, Version = 2 });
        db.Reservations.Add(new Reservation { Id = Guid.NewGuid(), EquipmentItemId = itemId, StudentId = studentId, Status = ReservationStatus.Returned, CreatedAtUtc = DateTime.UtcNow, Version = 3 });
        db.Reservations.Add(new Reservation { Id = Guid.NewGuid(), EquipmentItemId = itemId, StudentId = studentId, Status = ReservationStatus.Reserved, CreatedAtUtc = DateTime.UtcNow, Version = 1 });
        await db.SaveChangesAsync(); // must not throw
    }

    [Fact] // IT-CON-02a: deterministic stale update — the student cancels while staff's pickup is mid-flight
    public async Task Stale_pickup_is_rejected_when_the_reservation_changed_underneath_it()
    {
        var itemId = await ItemIdAsync("AUD-001");
        var student1 = await SignedInAsync(TestUsers.Student1);
        var staff = await SignedInAsync(TestUsers.Staff);
        var reservation = await ReserveOkAsync(student1, itemId);

        Factory.Interceptor.Arm(
            when: ctx => ctx.ChangeTracker.Entries<Reservation>()
                .Any(e => e.State == EntityState.Modified && e.Entity.Id == reservation.Id),
            sql: $"""
                  UPDATE "Reservations" SET "Status" = 'Cancelled', "Version" = "Version" + 1 WHERE "Id" = '{reservation.Id}'
                  """);

        var pickup = await staff.PostAsync($"/api/reservations/{reservation.Id}/pickup");

        Assert.True(Factory.Interceptor.Fired);
        Assert.Equal("stale_state", await AssertProblemAsync(pickup, HttpStatusCode.Conflict, "Conflict"));

        await using var db = NewDb();
        var saved = await db.Reservations.SingleAsync(r => r.Id == reservation.Id);
        Assert.Equal(ReservationStatus.Cancelled, saved.Status);
        Assert.DoesNotContain(await db.HistoryEvents.Where(h => h.ReservationId == reservation.Id).ToListAsync(), h => h.EventType == "PickedUp");
    }

    [Fact] // IT-CON-02b: real parallel cancel and pickup — exactly one wins, whichever it is
    public async Task Parallel_cancel_and_pickup_accept_exactly_one()
    {
        var itemId = await ItemIdAsync("AUD-002");
        var student1 = await SignedInAsync(TestUsers.Student1);
        var staff = await SignedInAsync(TestUsers.Staff);
        var reservation = await ReserveOkAsync(student1, itemId);

        var results = await Task.WhenAll(
            student1.PostAsync($"/api/reservations/{reservation.Id}/cancel"),
            staff.PostAsync($"/api/reservations/{reservation.Id}/pickup"));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);

        var winner = (await results.Single(r => r.IsSuccessStatusCode).Content.ReadFromJsonAsync<ReservationDto>())!;
        await using var db = NewDb();
        var saved = await db.Reservations.SingleAsync(r => r.Id == reservation.Id);
        Assert.Equal(winner.Status, saved.Status.ToString());
        Assert.Equal(2, await db.HistoryEvents.CountAsync(h => h.ReservationId == reservation.Id));
    }
}
