namespace FitMatrix.Api.Models;

public class AttributeDefinition
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public EquipmentCategory Category { get; set; } = null!;
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string ValueType { get; set; }
    public string? Unit { get; set; }
    public bool IsRequired { get; set; }
    public bool IsComparable { get; set; }
    public ICollection<ProductAttributeValue> Values { get; set; } = [];
}

public class ProductAttributeValue
{
    public Guid ProductId { get; set; }
    public EquipmentProduct Product { get; set; } = null!;
    public Guid AttributeDefinitionId { get; set; }
    public AttributeDefinition AttributeDefinition { get; set; } = null!;
    public decimal? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public bool? BooleanValue { get; set; }
}

public class ProductSource
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public EquipmentProduct Product { get; set; } = null!;
    public required string Provider { get; set; }
    public required string SourceUrl { get; set; }
    public string? ExternalId { get; set; }
    public string? LicenseName { get; set; }
    public DateTimeOffset LastCheckedAt { get; set; }
}

public class ProductSpecification
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public EquipmentProduct Product { get; set; } = null!;
    public required string Group { get; set; }
    public required string Name { get; set; }
    public required string NormalizedKey { get; set; }
    public required string Value { get; set; }
    public string? Unit { get; set; }
    public string? SourceUrl { get; set; }
    public DateTimeOffset LastCheckedAt { get; set; }
}
