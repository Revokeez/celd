using System.Net;
using System.Net.Http.Json;
using Celd.Api.Application;
using Celd.Api.Domain;
using Celd.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Celd.Tests.Integration;

/// <summary>
/// AC-10, BR-RES-004, DR-004, NFR-REL-002 — against real PostgreSQL.
/// The forced failure happens INSIDE the database (a trigger that rejects history inserts), after the
/// reservation row has already been written in the same transaction. That is the case a fake cannot show.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AtomicHistoryTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact] // IT-HIS-01 — every accepted change has a history event with actor and UTC time
    public async Task Full_lifecycle_records_one_event_per_change_with_actor_and_time()
    {
        var itemId = await ItemIdAsync("LAP-001");
        var student1 = await SignedInAsync(TestUsers.Student1);
        var staff = await SignedInAsync(TestUsers.Staff);

        var reservation = await ReserveOkAsync(student1, itemId);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/reservations/{reservation.Id}/pickup")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/reservations/{reservation.Id}/return")).StatusCode);

        var history = (await student1.Http.GetFromJsonAsync<List<HistoryEventDto>>($"/api/reservations/{reservation.Id}/history"))!;

        Assert.Equal(new[] { "Created", "PickedUp", "Returned" }, history.Select(h => h.EventType));
        Assert.Equal(new[] { "Reserved", "CheckedOut", "Returned" }, history.Select(h => h.ResultingStatus));
        Assert.Equal(new[] { "Student One", "Desk Staff", "Desk Staff" }, history.Select(h => h.ActorName));
        Assert.All(history, h => Assert.Equal(DateTimeKind.Utc, h.OccurredAtUtc.Kind));
        Assert.True(history.Zip(history.Skip(1)).All(p => p.First.OccurredAtUtc <= p.Second.OccurredAtUtc));
    }

    [Fact] // IT-TX-01a — forced history failure during Reserve leaves no reservation behind
    public async Task Failed_history_write_rolls_back_the_new_reservation()
    {
        var itemId = await ItemIdAsync("CAM-002");
        var student1 = await SignedInAsync(TestUsers.Student1);

        await ExecuteSqlAsync(FailHistoryInsertsSql);
        var response = await student1.PostAsync("/api/reservations", new { equipmentItemId = itemId });
        await ExecuteSqlAsync(RestoreHistoryInsertsSql);

        // The client is told it failed, never that it succeeded (UC-002 E6.1).
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        await using var db = NewDb();
        Assert.Equal(0, await db.Reservations.CountAsync(r => r.EquipmentItemId == itemId));
        Assert.Equal(0, await db.HistoryEvents.CountAsync());

        var item = await NewClient().Http.GetFromJsonAsync<EquipmentDetailDto>($"/api/equipment/{itemId}");
        Assert.True(item!.IsAvailable);
    }

    [Fact] // IT-TX-01b — forced history failure during Pickup leaves the status unchanged
    public async Task Failed_history_write_rolls_back_the_status_change()
    {
        var itemId = await ItemIdAsync("ACC-001");
        var student1 = await SignedInAsync(TestUsers.Student1);
        var staff = await SignedInAsync(TestUsers.Staff);
        var reservation = await ReserveOkAsync(student1, itemId);

        await ExecuteSqlAsync(FailHistoryInsertsSql);
        var response = await staff.PostAsync($"/api/reservations/{reservation.Id}/pickup");
        await ExecuteSqlAsync(RestoreHistoryInsertsSql);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await using var db = NewDb();
        var saved = await db.Reservations.SingleAsync(r => r.Id == reservation.Id);
        Assert.Equal(ReservationStatus.Reserved, saved.Status);
        Assert.Equal(1, saved.Version);
        Assert.Equal(1, await db.HistoryEvents.CountAsync(h => h.ReservationId == reservation.Id));

        // And the system still works normally afterwards.
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/reservations/{reservation.Id}/pickup")).StatusCode);
    }

    [Fact] // IT-REL-02 — NFR-REL-002, DR-005: records survive an application restart
    public async Task Records_survive_an_application_restart()
    {
        var itemId = await ItemIdAsync("CAM-001");
        var student1 = await SignedInAsync(TestUsers.Student1);
        var reservation = await ReserveOkAsync(student1, itemId);

        await Factory.DisposeAsync();
        var restarted = new CeldApiFactory(ConnectionString);
        Factory = restarted; // the base class disposes whichever factory is live
        var client = new ApiClient(restarted.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }));
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(TestUsers.Student1, TestUsers.Password)).StatusCode);

        var mine = (await client.Http.GetFromJsonAsync<List<ReservationDto>>("/api/reservations/mine"))!;
        Assert.Equal(reservation.Id, Assert.Single(mine).Id);
    }

    private const string FailHistoryInsertsSql = """
        CREATE OR REPLACE FUNCTION celd_test_fail_history() RETURNS trigger AS $$
        BEGIN RAISE EXCEPTION 'forced history failure (test)'; END; $$ LANGUAGE plpgsql;
        CREATE TRIGGER celd_test_fail_history BEFORE INSERT ON "HistoryEvents"
        FOR EACH ROW EXECUTE FUNCTION celd_test_fail_history();
        """;

    private const string RestoreHistoryInsertsSql = """
        DROP TRIGGER IF EXISTS celd_test_fail_history ON "HistoryEvents";
        """;

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
