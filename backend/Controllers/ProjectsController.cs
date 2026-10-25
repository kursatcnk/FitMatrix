using FitMatrix.Api.Data;
using FitMatrix.Api.DTOs;
using FitMatrix.Api.Models;
using FitMatrix.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitMatrix.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(FitMatrixDbContext dbContext) : ControllerBase
{
    private static readonly HashSet<string> SupportedCategories = ProductTaxonomy.SupportedCategories;
    private static readonly HashSet<string> SupportedEditorModels = ProductTaxonomy.SupportedEditorModels;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectSummaryDto>>> GetProjects(CancellationToken cancellationToken)
    {
        var projects = await dbContext.DeskProjects
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new ProjectSummaryDto(x.Id, x.Name, x.Items.Count, x.UpdatedAt))
            .ToListAsync(cancellationToken);
        return Ok(projects);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> GetProject(Guid id, CancellationToken cancellationToken)
    {
        var project = await dbContext.DeskProjects.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return project is null ? NotFound() : Ok(MapProject(project));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> CreateProject(ProjectUpsertRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null) return BadRequest(new { message = validation });

        var now = DateTimeOffset.UtcNow;
        var project = new DeskProject
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            DeskWidthCm = request.DeskWidthCm,
            DeskDepthCm = request.DeskDepthCm,
            DeskHeightCm = request.DeskHeightCm,
            CreatedAt = now,
            UpdatedAt = now,
            Items = CreateItems(request.Items)
        };
        ApplyRoom(project, request.Room);
        dbContext.DeskProjects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, MapProject(project));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> UpdateProject(Guid id, ProjectUpsertRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null) return BadRequest(new { message = validation });

        var project = await dbContext.DeskProjects.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (project is null) return NotFound();

        project.Name = request.Name.Trim();
        project.DeskWidthCm = request.DeskWidthCm;
        project.DeskDepthCm = request.DeskDepthCm;
        project.DeskHeightCm = request.DeskHeightCm;
        ApplyRoom(project, request.Room);
        project.UpdatedAt = DateTimeOffset.UtcNow;
        var previousItems = project.Items.ToList();
        dbContext.DeskProjectItems.RemoveRange(previousItems);
        project.Items.Clear();

        var replacementItems = CreateItems(request.Items);
        foreach (var item in replacementItems)
        {
            item.ProjectId = project.Id;
            project.Items.Add(item);
        }
        dbContext.DeskProjectItems.AddRange(replacementItems);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapProject(project));
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<ProjectDto>> DuplicateProject(Guid id, CancellationToken cancellationToken)
    {
        var source = await dbContext.DeskProjects
            .AsNoTracking()
            .Include(project => project.Items)
            .SingleOrDefaultAsync(project => project.Id == id, cancellationToken);
        if (source is null) return NotFound();

        var now = DateTimeOffset.UtcNow;
        var copy = new DeskProject
        {
            Id = Guid.NewGuid(),
            Name = source.Name.Length <= 151 ? $"{source.Name} — Kopya" : $"{source.Name[..151]} — Kopya",
            DeskWidthCm = source.DeskWidthCm,
            DeskDepthCm = source.DeskDepthCm,
            DeskHeightCm = source.DeskHeightCm,
            RoomWidthCm = source.RoomWidthCm,
            RoomDepthCm = source.RoomDepthCm,
            RoomWallGapCm = source.RoomWallGapCm,
            CreatedAt = now,
            UpdatedAt = now,
            Items = source.Items.OrderBy(item => item.SortOrder).Select(item => new DeskProjectItem
            {
                Id = Guid.NewGuid(),
                EquipmentProductId = item.EquipmentProductId,
                Name = item.Name,
                CategorySlug = item.CategorySlug,
                EditorModel = item.EditorModel,
                DisplayColor = item.DisplayColor,
                WidthCm = item.WidthCm,
                HeightCm = item.HeightCm,
                DepthCm = item.DepthCm,
                FootprintWidthCm = item.FootprintWidthCm,
                FootprintDepthCm = item.FootprintDepthCm,
                PositionXcm = item.PositionXcm,
                PositionZcm = item.PositionZcm,
                RotationDegrees = item.RotationDegrees,
                ElevationCm = item.ElevationCm,
                MountMode = item.MountMode,
                ScreenOrientation = item.ScreenOrientation,
                TiltDegrees = item.TiltDegrees,
                SortOrder = item.SortOrder
            }).ToList()
        };
        dbContext.DeskProjects.Add(copy);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetProject), new { id = copy.Id }, MapProject(copy));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteProject(Guid id, CancellationToken cancellationToken)
    {
        var project = await dbContext.DeskProjects.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (project is null) return NotFound();
        dbContext.DeskProjects.Remove(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static List<DeskProjectItem> CreateItems(IReadOnlyList<ProjectItemRequest> items) => items.Select((item, index) => new DeskProjectItem
    {
        Id = Guid.NewGuid(),
        EquipmentProductId = item.EquipmentProductId,
        Name = item.Name.Trim(),
        CategorySlug = item.CategorySlug,
        EditorModel = item.EditorModel,
        DisplayColor = item.DisplayColor,
        WidthCm = item.WidthCm,
        HeightCm = item.HeightCm,
        DepthCm = item.DepthCm,
        FootprintWidthCm = item.FootprintWidthCm,
        FootprintDepthCm = item.FootprintDepthCm,
        PositionXcm = item.PositionXcm,
        PositionZcm = item.PositionZcm,
        RotationDegrees = item.RotationDegrees,
        ElevationCm = item.ElevationCm,
        MountMode = item.MountMode,
        ScreenOrientation = item.ScreenOrientation,
        TiltDegrees = item.TiltDegrees,
        SortOrder = index
    }).ToList();

    private static string? Validate(ProjectUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Proje adı zorunludur.";
        if (request.Name.Length > 160) return "Proje adı en fazla 160 karakter olabilir.";
        if (request.DeskWidthCm is < 40 or > 500 || request.DeskDepthCm is < 30 or > 300 || request.DeskHeightCm is < 30 or > 150) return "Masa ölçüleri desteklenen aralığın dışında.";
        if (request.Room is { } room && (room.WidthCm is < 100 or > 1200 || room.DepthCm is < 100 or > 1200 || room.WallGapCm is < 0 or > 300)) return "Oda ölçüleri desteklenen aralığın dışında.";
        if (request.Items.Count > 200) return "Bir projede en fazla 200 ekipman bulunabilir.";
        if (request.Items.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 220)) return "Ekipman adı zorunludur ve en fazla 220 karakter olabilir.";
        if (request.Items.Any(x => x.WidthCm <= 0 || x.HeightCm <= 0 || x.DepthCm <= 0 || x.WidthCm > 1000 || x.HeightCm > 1000 || x.DepthCm > 1000)) return "Ekipman ölçüleri pozitif ve desteklenen aralıkta olmalıdır.";
        if (request.Items.Any(x => x.FootprintWidthCm <= 0 || x.FootprintDepthCm <= 0 || x.FootprintWidthCm > x.WidthCm || x.FootprintDepthCm > x.DepthCm)) return "Ekipman temas alanı gövde ölçülerini aşamaz.";
        if (request.Items.Any(x => !SupportedCategories.Contains(x.CategorySlug) || !SupportedEditorModels.Contains(x.EditorModel))) return "Ekipman kategorisi veya 3D model türü desteklenmiyor.";
        if (request.Items.Any(x => x.ElevationCm is < 0 or > 300 || Math.Abs(x.TiltDegrees) > 45)) return "Montaj yüksekliği veya eğim desteklenen aralığın dışında.";
        if (request.Items.Any(x => x.MountMode is not ("surface" or "vesa") || x.ScreenOrientation is not ("landscape" or "portrait"))) return "Montaj veya ekran yönü desteklenmiyor.";
        if (request.Items.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x.DisplayColor, "^#[0-9a-fA-F]{6}$"))) return "Ekipman rengi #RRGGBB biçiminde olmalıdır.";
        if (request.Items.Any(x => Math.Abs(x.PositionXcm) > 5000 || Math.Abs(x.PositionZcm) > 5000 || Math.Abs(x.RotationDegrees) > 36000)) return "Ekipman konumu veya rotasyonu desteklenen aralığın dışında.";
        return null;
    }

    private static ProjectDto MapProject(DeskProject project) => new(
        project.Id,
        project.Name,
        project.DeskWidthCm,
        project.DeskDepthCm,
        project.DeskHeightCm,
        project.CreatedAt,
        project.UpdatedAt,
        project.Items.OrderBy(x => x.SortOrder).Select(x => new ProjectItemDto(
            x.Id,
            x.EquipmentProductId,
            x.Name,
            x.CategorySlug,
            x.EditorModel,
            x.DisplayColor,
            x.WidthCm,
            x.HeightCm,
            x.DepthCm,
            x.FootprintWidthCm,
            x.FootprintDepthCm,
            x.PositionXcm,
            x.PositionZcm,
            x.RotationDegrees,
            x.ElevationCm,
            x.MountMode,
            x.ScreenOrientation,
            x.TiltDegrees)).ToList(),
        project is { RoomWidthCm: { } width, RoomDepthCm: { } depth, RoomWallGapCm: { } gap } ? new ProjectRoomDto(width, depth, gap) : null);

    private static void ApplyRoom(DeskProject project, ProjectRoomDto? room)
    {
        project.RoomWidthCm = room?.WidthCm;
        project.RoomDepthCm = room?.DepthCm;
        project.RoomWallGapCm = room?.WallGapCm;
    }
}
