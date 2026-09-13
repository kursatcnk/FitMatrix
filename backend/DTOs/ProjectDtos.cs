namespace FitMatrix.Api.DTOs;

public record ProjectItemRequest(
    Guid? EquipmentProductId,
    string Name,
    string CategorySlug,
    string EditorModel,
    string DisplayColor,
    decimal WidthCm,
    decimal HeightCm,
    decimal DepthCm,
    decimal FootprintWidthCm,
    decimal FootprintDepthCm,
    decimal PositionXcm,
    decimal PositionZcm,
    decimal RotationDegrees,
    decimal ElevationCm = 0,
    string MountMode = "surface",
    string ScreenOrientation = "landscape",
    decimal TiltDegrees = 0);

public record ProjectUpsertRequest(
    string Name,
    decimal DeskWidthCm,
    decimal DeskDepthCm,
    decimal DeskHeightCm,
    IReadOnlyList<ProjectItemRequest> Items);

public record ProjectSummaryDto(Guid Id, string Name, int ItemCount, DateTimeOffset UpdatedAt);

public record ProjectItemDto(
    Guid Id,
    Guid? EquipmentProductId,
    string Name,
    string CategorySlug,
    string EditorModel,
    string DisplayColor,
    decimal WidthCm,
    decimal HeightCm,
    decimal DepthCm,
    decimal FootprintWidthCm,
    decimal FootprintDepthCm,
    decimal PositionXcm,
    decimal PositionZCm,
    decimal RotationDegrees,
    decimal ElevationCm,
    string MountMode,
    string ScreenOrientation,
    decimal TiltDegrees);

public record ProjectDto(
    Guid Id,
    string Name,
    decimal DeskWidthCm,
    decimal DeskDepthCm,
    decimal DeskHeightCm,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ProjectItemDto> Items);
