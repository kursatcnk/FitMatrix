namespace FitMatrix.Api.DTOs;

public sealed record ProductDiscoveryResultDto(
    Guid Id,
    string Brand,
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
    string DataQuality,
    bool IsVerified,
    int SourceCount,
    int MatchScore);

public sealed record ExternalSpecificationDto(string Group, string Name, string Value, string? Unit);

public sealed record ExternalProductCandidateDto(
    string Provider,
    string ProductName,
    string? Brand,
    string? ModelName,
    string? ManufacturerPartNumber,
    string? Gtin,
    decimal? WidthCm,
    decimal? HeightCm,
    decimal? DepthCm,
    decimal? WeightKg,
    string? SourceUrl,
    string MeasurementStatus,
    string MeasurementNote,
    IReadOnlyList<ExternalSpecificationDto> Specifications,
    string SuggestedCategory = "accessory",
    string SuggestedEditorModel = "generic",
    string SuggestedColor = "#3b4240");

public sealed record ProductProviderStatusDto(
    string Provider,
    bool IsConfigured,
    string Message);

public sealed record ProductDiscoveryResponseDto(
    string Query,
    IReadOnlyList<ProductDiscoveryResultDto> LocalMatches,
    ExternalProductCandidateDto? ExternalMatch,
    IReadOnlyList<ExternalProductCandidateDto> ExternalMatches,
    ProductProviderStatusDto Provider);
