using System.Net;
using System.Net.Http.Json;
using Celd.Api.Application;
using Celd.Api.Domain;
using Celd.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Celd.Tests.Integration;

/// <summary>AC-02, AC-05, AC-07 and the HTTP side of BR-AUT-001..003, NFR-SEC-001..002.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthorizationApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    // ---------------- AC-02 sign in / sign out ----------------

    [Theory] // IT-AUT-01 — FR-AUT-001, FR-AUT-003
    [InlineData(TestUsers.Student1, "Student")]
    [InlineData(TestUsers.Staff, "Staff")]
    public async Task Valid_accounts_sign_in_and_see_their_role(string userName, string role)
    {
        var client = await SignedInAsync(userName);

        var me = await client.Http.GetFromJsonAsync<CurrentUserDto>("/api/auth/me");
        Assert.Equal(role, me!.Role);
    }

    [Fact] // IT-AUT-01 — FR-AUT-001: the response does not reveal which credential was wrong
    public async Task Wrong_password_and_unknown_user_get_identical_responses()
    {
        var wrongPassword = await NewClient().LoginAsync(TestUsers.Student1, "not-the-password");
        var unknownUser = await NewClient().LoginAsync("nobody", TestUsers.Password);

        Assert.Equal("invalid_credentials", await AssertProblemAsync(wrongPassword, HttpStatusCode.Unauthorized, "Unauthenticated"));
        Assert.Equal("invalid_credentials", await AssertProblemAsync(unknownUser, HttpStatusCode.Unauthorized, "Unauthenticated"));
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(), await unknownUser.Content.ReadAsStringAsync());
    }

    [Fact] // IT-AUT-02 — FR-AUT-002, AC-02: a copied session cookie stops working after sign-out
    public async Task Ended_session_cannot_be_replayed()
    {
        var ownerJar = new CookieContainer();
        var owner = NewClient(ownerJar);
        Assert.Equal(HttpStatusCode.OK, (await owner.LoginAsync(TestUsers.Student1, TestUsers.Password)).StatusCode);

        // Copy the session cookie into a second "browser", as an attacker or a stale tab would hold it.
        var session = ownerJar.GetCookies(BaseUri)["celd.session"];
        Assert.NotNull(session);
        var replayJar = new CookieContainer();
        replayJar.Add(BaseUri, new Cookie(session.Name, session.Value));
        var replay = NewClient(replayJar);
        Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/auth/logout")).StatusCode);

        await AssertProblemAsync(await replay.GetAsync("/api/reservations/mine"), HttpStatusCode.Unauthorized, "Unauthenticated");
        await AssertProblemAsync(await owner.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "Unauthenticated");
    }

    [Fact] // MAN-SEC-01, automated part — NFR-SEC-002: cookie flags outside Development
    public async Task Session_cookie_is_HttpOnly_Secure_and_SameSite_Strict()
    {
        // A client WITHOUT a cookie jar, so the raw Set-Cookie header is visible.
        var raw = Factory.CreateDefaultClient(BaseUri);

        var csrfResponse = await raw.GetAsync("/api/auth/csrf");
        var csrfCookie = csrfResponse.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("celd.csrf=")).Split(';')[0];
        var token = (await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenDto>())!.Token;

        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { userName = TestUsers.Student1, password = TestUsers.Password }),
        };
        login.Headers.Add("Cookie", csrfCookie);
        login.Headers.Add("X-CSRF-TOKEN", token);
        var response = await raw.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var header = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("celd.session=")).ToLowerInvariant();
        Assert.Contains("httponly", header);
        Assert.Contains("secure", header);
        Assert.Contains("samesite=strict", header);
    }

    // ---------------- Role checks (AC-02, AC-07, BR-AUT-003) ----------------

    [Fact] // UC-002 E4.1 / E4.2
    public async Task Visitors_get_401_and_staff_get_403_when_reserving()
    {
        var itemId = await ItemIdAsync("CAM-001");

        var visitor = await NewClient().PostAsync("/api/reservations", new { equipmentItemId = itemId });
        var staff = await (await SignedInAsync(TestUsers.Staff)).PostAsync("/api/reservations", new { equipmentItemId = itemId });

        await AssertProblemAsync(visitor, HttpStatusCode.Unauthorized, "Unauthenticated");
        await AssertProblemAsync(staff, HttpStatusCode.Forbidden, "Forbidden");
    }

    [Fact] // IT-AUT-05 — AC-07
    public async Task Students_and_visitors_cannot_record_pickup_or_return_or_see_the_staff_queue()
    {
        var student1 = await SignedInAsync(TestUsers.Student1);
        var reservation = await ReserveOkAsync(student1, await ItemIdAsync("CAM-001"));
        var visitor = NewClient();

        await AssertProblemAsync(await student1.PostAsync($"/api/reservations/{reservation.Id}/pickup"), HttpStatusCode.Forbidden, "Forbidden");
        await AssertProblemAsync(await student1.PostAsync($"/api/reservations/{reservation.Id}/return"), HttpStatusCode.Forbidden, "Forbidden");
        await AssertProblemAsync(await visitor.PostAsync($"/api/reservations/{reservation.Id}/pickup"), HttpStatusCode.Unauthorized, "Unauthenticated");
        await AssertProblemAsync(await student1.GetAsync("/api/staff/reservations"), HttpStatusCode.Forbidden, "Forbidden");
        await AssertProblemAsync(await visitor.GetAsync("/api/staff/reservations"), HttpStatusCode.Unauthorized, "Unauthenticated");

        await using var db = NewDb();
        Assert.Equal(ReservationStatus.Reserved, (await db.Reservations.SingleAsync(r => r.Id == reservation.Id)).Status);
    }

    // ---------------- AC-05 ownership ----------------

    [Fact] // IT-AUT-04 — BR-AUT-002, NFR-SEC-001
    public async Task Student_cannot_read_or_cancel_another_students_reservation()
    {
        var student1 = await SignedInAsync(TestUsers.Student1);
        var student2 = await SignedInAsync(TestUsers.Student2);
        var reservation = await ReserveOkAsync(student1, await ItemIdAsync("CAM-001"));

        var read = await student2.GetAsync($"/api/reservations/{reservation.Id}");
        var history = await student2.GetAsync($"/api/reservations/{reservation.Id}/history");
        var cancel = await student2.PostAsync($"/api/reservations/{reservation.Id}/cancel");
        var mine = await student2.Http.GetFromJsonAsync<List<ReservationDto>>("/api/reservations/mine");

        foreach (var response in new[] { read, history, cancel })
        {
            Assert.Equal("reservation_not_found", await AssertProblemAsync(response, HttpStatusCode.NotFound, "NotFound"));
            Assert.DoesNotContain("Student One", await response.Content.ReadAsStringAsync());
        }

        Assert.Empty(mine!);
        await using var db = NewDb();
        Assert.Equal(ReservationStatus.Reserved, (await db.Reservations.SingleAsync(r => r.Id == reservation.Id)).Status);
    }

    [Fact] // IT-AUT-03 — BR-AUT-001: a student id or role smuggled into the body is ignored
    public async Task Reservation_owner_comes_from_the_session_not_the_request_body()
    {
        var student1 = await SignedInAsync(TestUsers.Student1);

        var response = await student1.PostAsync("/api/reservations", new
        {
            equipmentItemId = await ItemIdAsync("CAM-001"),
            studentId = await UserIdAsync(TestUsers.Student2),
            role = "Staff",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ReservationDto>())!;
        Assert.Equal(await UserIdAsync(TestUsers.Student1), created.StudentId);
    }

    // ---------------- NFR-SEC-002 CSRF ----------------

    [Fact] // IT-SEC-01
    public async Task State_changing_request_without_csrf_token_is_refused()
    {
        var student1 = await SignedInAsync(TestUsers.Student1);

        var response = await student1.PostAsync("/api/reservations", new { equipmentItemId = await ItemIdAsync("CAM-001") }, withCsrf: false);

        Assert.Equal("csrf_failed", await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Validation"));
        await using var db = NewDb();
        Assert.Equal(0, await db.Reservations.CountAsync());
    }
}
