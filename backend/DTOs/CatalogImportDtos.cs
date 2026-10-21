namespace FitMatrix.Api.DTOs;

public sealed record CatalogImportRequest(
    string Provider,
    string? LicenseName,
    IReadOnlyList<CatalogImportItemDto> Products);

public sealed record CatalogImportItemDto(
    string BrandName,
    string CategorySlug,
    string DisplayName,
    string ModelName,
    string? ManufacturerPartNumber,
    string? Gtin,
    decimal WidthCm,
    decimal HeightCm,
    decimal DepthCm,
    decimal? FootprintWidthCm,
    decimal? FootprintDepthCm,
    decimal? WeightKg,
    string? EditorModel,
    string? DisplayColor,
    string? ImageUrl,
    string SourceUrl,
    string? ExternalId,
    bool IsVerified,
    IReadOnlyList<CatalogImportAttributeDto>? Attributes);

public sealed record CatalogImportAttributeDto(
    string Code,
    decimal? NumericValue,
    string? TextValue,
    bool? BooleanValue);

public sealed record CatalogImportErrorDto(int Row, string Product, string Message);

public sealed record CatalogImportResultDto(
    int Received,
    int Created,
    int Updated,
    int Rejected,
    IReadOnlyList<CatalogImportErrorDto> Errors);
