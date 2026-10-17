namespace FitMatrix.Api.DTOs;

public sealed record CatalogHarvestStartRequest(
    IReadOnlyList<string>? Lists,
    IReadOnlyList<string>? Queries,
    bool RequireVerified = true,
    int DelaySeconds = 6,
    int? MaxQueries = null);

public sealed record CatalogHarvestListDto(string Name, int QueryCount);

public sealed record ManufacturerHarvestStartRequest(
    IReadOnlyList<string>? Sources,
    int DelaySeconds = 3,
    int? MaxPagesPerSource = null);

public sealed record ManufacturerSourceDto(string Name, string Brand, IReadOnlyList<string> Sitemaps);

public sealed record CatalogHarvestEntryDto(
    string Query,
    string Outcome,
    string? ProductName,
    string? Message,
    DateTimeOffset FinishedAt);

public sealed record CatalogHarvestStatusDto(
    bool IsRunning,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Total,
    int Processed,
    int Imported,
    int Updated,
    int AlreadyInCatalog,
    int NoVerifiedMatch,
    int Failed,
    string? CurrentQuery,
    IReadOnlyList<CatalogHarvestEntryDto> Recent);
