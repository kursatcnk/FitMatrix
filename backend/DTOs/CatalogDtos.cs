namespace FitMatrix.Api.DTOs;

public record CategoryDto(Guid Id, string Name, string Slug, string EditorModel, int ProductCount);

public record CatalogProductDto(
    Guid Id,
    string Brand,
    string Category,
    string CategorySlug,
    string DisplayName,
    string ModelName,
    string? ManufacturerPartNumber,
    string? Gtin,
    decimal WidthCm,
    decimal HeightCm,
    decimal DepthCm,
    decimal FootprintWidthCm,
    decimal FootprintDepthCm,
    decimal? WeightKg,
    string EditorModel,
    string DisplayColor,
    string? ImageUrl,
    bool IsVerified,
    string DataQuality);

public record ProductAttributeDto(string Code, string Name, string ValueType, string? Unit, decimal? NumericValue, string? TextValue, bool? BooleanValue);
public record ProductSourceDto(string Provider, string SourceUrl, string? ExternalId, string? LicenseName, DateTimeOffset LastCheckedAt);
public record ProductSpecificationDto(string Group, string Name, string Value, string? Unit, string? SourceUrl, DateTimeOffset LastCheckedAt);
public record CatalogProductDetailDto(CatalogProductDto Product, IReadOnlyList<ProductAttributeDto> Attributes, IReadOnlyList<ProductSpecificationDto> Specifications, IReadOnlyList<ProductSourceDto> Sources);

public sealed record DiscoverySpecificationDto(string Group, string Name, string Value, string? Unit);

public sealed record DiscoveryCatalogRequestDto(
    string Provider,
    string ProductName,
    string Brand,
    string ModelName,
    string? ManufacturerPartNumber,
    string? Gtin,
    decimal WidthCm,
    decimal HeightCm,
    decimal DepthCm,
    decimal FootprintWidthCm,
    decimal FootprintDepthCm,
    decimal? WeightKg,
    string CategorySlug,
    string EditorModel,
    string DisplayColor,
    string SourceUrl,
    string MeasurementStatus,
    IReadOnlyList<DiscoverySpecificationDto>? Specifications);
