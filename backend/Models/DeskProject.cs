namespace FitMatrix.Api.Models;

public class DeskProject
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public decimal DeskWidthCm { get; set; }
    public decimal DeskDepthCm { get; set; }
    public decimal DeskHeightCm { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<DeskProjectItem> Items { get; set; } = [];
}

public class DeskProjectItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public DeskProject Project { get; set; } = null!;
    public Guid? EquipmentProductId { get; set; }
    public EquipmentProduct? EquipmentProduct { get; set; }
    public required string Name { get; set; }
    public required string CategorySlug { get; set; }
    public required string EditorModel { get; set; }
    public required string DisplayColor { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal DepthCm { get; set; }
    public decimal FootprintWidthCm { get; set; }
    public decimal FootprintDepthCm { get; set; }
    public decimal PositionXcm { get; set; }
    public decimal PositionZcm { get; set; }
    public decimal RotationDegrees { get; set; }
    public decimal ElevationCm { get; set; }
    public required string MountMode { get; set; } = "surface";
    public required string ScreenOrientation { get; set; } = "landscape";
    public decimal TiltDegrees { get; set; }
    public int SortOrder { get; set; }
}
