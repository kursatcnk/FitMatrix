using FitMatrix.Api.DTOs;
using FitMatrix.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FitMatrix.Api.Controllers;

[ApiController]
[Route("api/admin/catalog")]
public sealed class CatalogImportController(
    CatalogImportService importService,
    IHostEnvironment environment) : ControllerBase
{
    [HttpPost("import")]
    public async Task<ActionResult<CatalogImportResultDto>> Import(
        [FromBody] CatalogImportRequest request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) return NotFound();

        var result = await importService.ImportAsync(request, cancellationToken);
        return result.Rejected == result.Received && result.Received > 0
            ? BadRequest(result)
            : Ok(result);
    }
}
