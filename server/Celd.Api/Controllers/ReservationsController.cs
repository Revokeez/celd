using Celd.Api.Application;
using Celd.Api.Domain;
using Celd.Api.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Celd.Api.Controllers;

/// <summary>
/// Thin HTTP adapter. Role attributes are the first check (BR-AUT-003); the Central Application
/// Service rechecks roles and the Reservation Service checks ownership.
/// There are deliberately no DELETE endpoints (BR-RES-008).
/// </summary>
[ApiController]
[Route("api/reservations")]
[Authorize]
[AutoValidateAntiforgeryToken] // NFR-SEC-002: every POST needs the RequestVerificationToken header; GETs are exempt.
public sealed class ReservationsController(CentralApplicationService app) : ControllerBase
{
    // UC-002
    [HttpPost]
    [Authorize(Roles = nameof(Role.Student))]
    public async Task<IActionResult> Create(CreateReservationRequest request, CancellationToken ct) =>
        (await app.ReserveAsync(request.EquipmentItemId!.Value, ct))
            .ToActionResult(r => CreatedAtAction(nameof(Get), new { id = r.Id }, r));

    // UC-003
    [HttpGet("mine")]
    [Authorize(Roles = nameof(Role.Student))]
    public async Task<IActionResult> Mine(CancellationToken ct) =>
        (await app.ListMyReservationsAsync(ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        (await app.GetReservationAsync(id, ct)).ToActionResult();

    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) =>
        (await app.GetHistoryAsync(id, ct)).ToActionResult();

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = nameof(Role.Student))]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) =>
        (await app.CancelAsync(id, ct)).ToActionResult();

    // UC-004
    [HttpPost("{id:guid}/pickup")]
    [Authorize(Roles = nameof(Role.Staff))]
    public async Task<IActionResult> Pickup(Guid id, CancellationToken ct) =>
        (await app.RecordPickupAsync(id, ct)).ToActionResult();

    // UC-005
    [HttpPost("{id:guid}/return")]
    [Authorize(Roles = nameof(Role.Staff))]
    public async Task<IActionResult> Return(Guid id, CancellationToken ct) =>
        (await app.RecordReturnAsync(id, ct)).ToActionResult();
}

/// <summary>UC-004 step 2: the Staff queue, optionally filtered by status.</summary>
[ApiController]
[Route("api/staff/reservations")]
[Authorize(Roles = nameof(Role.Staff))]
[AutoValidateAntiforgeryToken] // GET-only today; protects any state-changing action added later.
public sealed class StaffReservationsController(CentralApplicationService app) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        ReservationStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ReservationStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Problems.ToResult(OperationError.Validation(
                    "invalid_status_filter", "Status must be Reserved, CheckedOut, Cancelled or Returned."));
            }

            filter = parsed;
        }

        return (await app.ListStaffQueueAsync(filter, ct)).ToActionResult();
    }
}
