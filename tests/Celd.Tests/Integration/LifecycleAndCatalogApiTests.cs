using System.Net;
using System.Net.Http.Json;
using Celd.Api.Application;
using Celd.Tests.Support;

namespace Celd.Tests.Integration;

/// <summary>AC-01, AC-03, AC-06, AC-08, AC-09 and FR-ERR-001 through the real HTTP pipeline.</summary>
[Collection(PostgresCollection.Name)]
public sealed class LifecycleAndCatalogApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    // ---------------- AC-01 catalog ----------------

    [Fact] // IT-CAT-01 — FR-CAT-001
    public async Task Visitor_lists_seeded_catalog_with_availability()
    {
        var items = (await NewClient().Http.GetFromJsonAsync<List<EquipmentSummaryDto>>("/api/equipment"))!;

        Assert.True(items.Count >= 6);
        Assert.Equal(items.Count, items.Select(i => i.AssetCode).Distinct().Count());
        Assert.All(items, i => Assert.True(i.IsAvailable));
    }

    [Fact] // IT-CAT-02 — FR-CAT-002, UC-001 A2.1/A2.2/A4.1
    public async Task Catalog_search_and_category_filter_including_empty_result()
    {
        var client = NewClient().Http;

        var cameras = (await client.GetFromJsonAsync<List<EquipmentSummaryDto>>("/api/equipment?search=CAMERA"))!;
        var audio = (await client.GetFromJsonAsync<List<EquipmentSummaryDto>>("/api/equipment?category=audio"))!;
        var combined = (await client.GetFromJsonAsync<List<EquipmentSummaryDto>>("/api/equipment?search=recorder&category=Audio"))!;
        var none = (await client.GetFromJsonAsync<List<EquipmentSummaryDto>>("/api/equipment?search=zzz-no-match"))!;

        Assert.Equal(new[] { "CAM-001", "CAM-002" }, cameras.Select(i => i.AssetCode).OrderBy(c => c));
        Assert.All(audio, i => Assert.Equal("Audio", i.Category));
        Assert.Equal("AUD-002", Assert.Single(combined).AssetCode);
        Assert.Empty(none);
    }

    [Fact] // IT-CAT-03 — FR-CAT-003, UC-001 E6.1
    public async Task Missing_item_is_a_controlled_not_found()
    {
        var response = await NewClient().GetAsync($"/api/equipment/{Guid.NewGuid()}");
        Assert.Equal("equipment_not_found", await AssertProblemAsync(response, HttpStatusCode.NotFound, "NotFound"));
    }

    // ---------------- AC-03 reserve ----------------

    [Fact] // IT-RES-01 — AC-03
    public async Task Reserved_item_becomes_unavailable_to_another_student()
    {
        var itemId = await ItemIdAsync("CAM-001");
        var student1 = await SignedInAsync(TestUsers.Student1);
        var student2 = await SignedInAsync(TestUsers.Student2);

        var created = await ReserveOkAsync(student1, itemId);
        Assert.Equal("Reserved", created.Status);

        var detail = await NewClient().Http.GetFromJsonAsync<EquipmentDetailDto>($"/api/equipment/{itemId}");
        Assert.False(detail!.IsAvailable);

        var second = await student2.PostAsync("/api/reservations", new { equipmentItemId = itemId });
        Assert.Equal("item_unavailable", await AssertProblemAsync(second, HttpStatusCode.Conflict, "Conflict"));
    }

    [Fact] // FR-ERR-001 — validation category
    public async Task Missing_item_id_is_a_validation_problem()
    {
        var student1 = await SignedInAsync(TestUsers.Student1);
        var response = await student1.PostAsync("/api/reservations", new { });
        Assert.Equal("invalid_request", await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Validation"));
    }

    // ---------------- AC-06 cancel ----------------

    [Fact] // IT-RES-03 — AC-06
    public async Task Owner_cancels_and_record_stays_in_history_and_item_is_available()
    {
        var itemId = await ItemIdAsync("LAP-001");
        var student1 = await SignedInAsync(TestUsers.Student1);
        var created = await ReserveOkAsync(student1, itemId);

        var cancel = await student1.PostAsync($"/api/reservations/{created.Id}/cancel");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<ReservationDto>())!.Status);

        var mine = (await student1.Http.GetFromJsonAsync<List<ReservationDto>>("/api/reservations/mine"))!;
        var record = Assert.Single(mine);
        Assert.False(record.IsActive);

        var history = (await student1.Http.GetFromJsonAsync<List<HistoryEventDto>>($"/api/reservations/{created.Id}/history"))!;
        Assert.Equal(new[] { "Created", "Cancelled" }, history.Select(h => h.EventType));

        Assert.True((await NewClient().Http.GetFromJsonAsync<EquipmentDetailDto>($"/api/equipment/{itemId}"))!.IsAvailable);
    }

    // ---------------- AC-07 / AC-08 pickup and return ----------------

    [Fact] // IT-RES-04/05/06/07 — AC-07, AC-08, FR-RES-004
    public async Task Staff_queue_pickup_return_and_item_can_be_reserved_again()
    {
        var itemId = await ItemIdAsync("PRJ-001");
        var student1 = await SignedInAsync(TestUsers.Student1);
        var student2 = await SignedInAsync(TestUsers.Student2);
        var staff = await SignedInAsync(TestUsers.Staff);
        var created = await ReserveOkAsync(student1, itemId);

        var queue = (await staff.Http.GetFromJsonAsync<List<ReservationDto>>("/api/staff/reservations?status=Reserved"))!;
        var row = Assert.Single(queue);
        Assert.Equal(("Student One", "Portable Projector"), (row.StudentName, row.ItemName));

        var pickup = await staff.PostAsync($"/api/reservations/{created.Id}/pickup");
        Assert.Equal("CheckedOut", (await pickup.Content.ReadFromJsonAsync<ReservationDto>())!.Status);
        Assert.Empty((await staff.Http.GetFromJsonAsync<List<ReservationDto>>("/api/staff/reservations?status=Reserved"))!);

        var returned = await staff.PostAsync($"/api/reservations/{created.Id}/return");
        Assert.Equal("Returned", (await returned.Content.ReadFromJsonAsync<ReservationDto>())!.Status);
        Assert.True((await NewClient().Http.GetFromJsonAsync<EquipmentDetailDto>($"/api/equipment/{itemId}"))!.IsAvailable);

        await ReserveOkAsync(student2, itemId); // BR-RES-005
    }

    [Fact] // FR-RES-004 — bad filter is a validation problem
    public async Task Unknown_status_filter_is_rejected()
    {
        var staff = await SignedInAsync(TestUsers.Staff);
        var response = await staff.GetAsync("/api/staff/reservations?status=Lost");
        Assert.Equal("invalid_status_filter", await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Validation"));
    }

    // ---------------- AC-09 invalid / repeated / stale ----------------

    [Fact] // IT-RES-08 — AC-09, BR-RES-006
    public async Task Invalid_and_repeated_transitions_are_conflicts_with_no_extra_history()
    {
        var itemId = await ItemIdAsync("ACC-001");
        var student1 = await SignedInAsync(TestUsers.Student1);
        var staff = await SignedInAsync(TestUsers.Staff);
        var created = await ReserveOkAsync(student1, itemId);

        // Return before pickup is illegal.
        Assert.Equal("invalid_transition", await AssertProblemAsync(
            await staff.PostAsync($"/api/reservations/{created.Id}/return"), HttpStatusCode.Conflict, "Conflict"));

        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/reservations/{created.Id}/pickup")).StatusCode);

        // Repeated pickup and cancel-after-pickup are illegal.
        Assert.Equal("invalid_transition", await AssertProblemAsync(
            await staff.PostAsync($"/api/reservations/{created.Id}/pickup"), HttpStatusCode.Conflict, "Conflict"));
        Assert.Equal("invalid_transition", await AssertProblemAsync(
            await student1.PostAsync($"/api/reservations/{created.Id}/cancel"), HttpStatusCode.Conflict, "Conflict"));

        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/reservations/{created.Id}/return")).StatusCode);

        // Repeated return.
        Assert.Equal("invalid_transition", await AssertProblemAsync(
            await staff.PostAsync($"/api/reservations/{created.Id}/return"), HttpStatusCode.Conflict, "Conflict"));

        var history = (await staff.Http.GetFromJsonAsync<List<HistoryEventDto>>($"/api/reservations/{created.Id}/history"))!;
        Assert.Equal(new[] { "Created", "PickedUp", "Returned" }, history.Select(h => h.EventType));
    }

    [Fact] // BR-RES-008 — no API operation removes reservations
    public async Task Reservations_cannot_be_deleted_through_the_api()
    {
        var student1 = await SignedInAsync(TestUsers.Student1);
        var created = await ReserveOkAsync(student1, await ItemIdAsync("CAM-002"));

        var response = await student1.Http.DeleteAsync($"/api/reservations/{created.Id}");

        Assert.True(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.OK, (await student1.GetAsync($"/api/reservations/{created.Id}")).StatusCode);
    }
}
