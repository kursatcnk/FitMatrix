using FitMatrix.Api.DTOs;
using FitMatrix.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FitMatrix.Api.Controllers;

[ApiController]
[Route("api/admin/catalog/harvest")]
public sealed class CatalogHarvestController(
    CatalogHarvestService harvest,
    IHostEnvironment environment) : ControllerBase
{
    [HttpGet("lists")]
    public ActionResult<IReadOnlyList<CatalogHarvestListDto>> GetLists() =>
        environment.IsDevelopment() ? Ok(harvest.GetLists()) : NotFound();

    [HttpGet]
    public ActionResult<CatalogHarvestStatusDto> GetStatus() =>
        environment.IsDevelopment() ? Ok(harvest.GetStatus()) : NotFound();

    [HttpPost]
    public ActionResult<CatalogHarvestStatusDto> Start([FromBody] CatalogHarvestStartRequest request)
    {
        if (!environment.IsDevelopment()) return NotFound();
        if (request.DelaySeconds is < 2 or > 120)
            return BadRequest(new { message = "Sorgular arası bekleme 2–120 saniye olmalıdır." });

        var queries = harvest.ResolveQueries(request);
        if (queries.Count == 0) return BadRequest(new { message = "Çalıştırılacak model sorgusu bulunamadı." });
        if (!harvest.TryStart(queries, request.RequireVerified, TimeSpan.FromSeconds(request.DelaySeconds)))
            return Conflict(new { message = "Zaten çalışan bir katalog toplama işi var." });

        return Accepted(harvest.GetStatus());
    }

    [HttpGet("manufacturers")]
    public ActionResult<IReadOnlyList<ManufacturerSourceDto>> GetManufacturers() =>
        environment.IsDevelopment()
            ? Ok(harvest.GetManufacturers().Select(source => new ManufacturerSourceDto(source.Name, source.Brand, source.Sitemaps)).ToList())
            : NotFound();

    [HttpPost("manufacturers")]
    public ActionResult<CatalogHarvestStatusDto> StartManufacturers([FromBody] ManufacturerHarvestStartRequest request)
    {
        if (!environment.IsDevelopment()) return NotFound();
        if (request.DelaySeconds is < 1 or > 120)
            return BadRequest(new { message = "Sayfalar arası bekleme 1–120 saniye olmalıdır." });

        var sources = harvest.GetManufacturers()
            .Where(source => request.Sources is not { Count: > 0 } || request.Sources.Contains(source.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (sources.Count == 0) return BadRequest(new { message = "Tanımlı üretici kaynağı bulunamadı." });
        if (!harvest.TryStartManufacturers(sources, TimeSpan.FromSeconds(request.DelaySeconds), request.MaxPagesPerSource))
            return Conflict(new { message = "Zaten çalışan bir katalog toplama işi var." });

        return Accepted(harvest.GetStatus());
    }

    // Writes the current catalog as the snapshot shipped with the repository (backend/Data/Catalog).
    [HttpPost("/api/admin/catalog/snapshot")]
    public async Task<ActionResult<object>> ExportSnapshot(
        [FromServices] FitMatrix.Api.Data.FitMatrixDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) return NotFound();
        var document = await FitMatrix.Api.Data.CatalogSnapshot.ExportAsync(dbContext, cancellationToken);
        var projectRoot = System.IO.File.Exists(Path.Combine(environment.ContentRootPath, "FitMatrix.Api.csproj"))
            ? environment.ContentRootPath
            : Path.Combine(environment.ContentRootPath, "backend");
        var target = Path.Combine(projectRoot, "Data", "Catalog", "catalog-snapshot.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await System.IO.File.WriteAllTextAsync(target, FitMatrix.Api.Data.CatalogSnapshot.Serialize(document), cancellationToken);
        return Ok(new { document.Version, document.ProductCount, path = target });
    }

    [HttpDelete]
    public IActionResult Cancel()
    {
        if (!environment.IsDevelopment()) return NotFound();
        return harvest.Cancel() ? Accepted() : NoContent();
    }
}
