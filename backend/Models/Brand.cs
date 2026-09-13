namespace FitMatrix.Api.Models;

public class Brand
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? WebsiteUrl { get; set; }
    public ICollection<EquipmentProduct> Products { get; set; } = [];
}
