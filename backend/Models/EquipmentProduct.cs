namespace FitMatrix.Api.Models;

public class EquipmentProduct
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
    public Guid CategoryId { get; set; }
    public EquipmentCategory Category { get; set; } = null!;
    public required string DisplayName { get; set; }
    public required string ModelName { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public string? Gtin { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal DepthCm { get; set; }
    public decimal FootprintWidthCm { get; set; }
    public decimal FootprintDepthCm { get; set; }
    public decimal? WeightKg { get; set; }
    public required string EditorModel { get; set; }
    public required string DisplayColor { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsVerified { get; set; }
    public required string DataQuality { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<ProductAttributeValue> AttributeValues { get; set; } = [];
    public ICollection<ProductSource> Sources { get; set; } = [];
    public ICollection<ProductSpecification> Specifications { get; set; } = [];
}
