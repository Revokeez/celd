using Celd.Api.Application;
using Celd.Api.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Celd.Api.Controllers;

/// <summary>UC-001. Public: Visitors may browse without signing in.</summary>
[ApiController]
[Route("api/equipment")]
[AllowAnonymous]
public sealed class EquipmentController(CentralApplicationService app) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EquipmentSummaryDto>>> List(
        [FromQuery] string? search, [FromQuery] string? category, CancellationToken ct) =>
        Ok(await app.ListEquipmentAsync(search, category, ct));

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<string>>> Categories(CancellationToken ct) =>
        Ok(await app.ListCategoriesAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        (await app.GetEquipmentAsync(id, ct)).ToActionResult();
}
