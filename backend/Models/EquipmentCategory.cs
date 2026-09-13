namespace FitMatrix.Api.Models;

public class EquipmentCategory
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public required string EditorModel { get; set; }
    public Guid? ParentId { get; set; }
    public EquipmentCategory? Parent { get; set; }
    public ICollection<EquipmentCategory> Children { get; set; } = [];
    public ICollection<EquipmentProduct> Products { get; set; } = [];
    public ICollection<AttributeDefinition> AttributeDefinitions { get; set; } = [];
}
