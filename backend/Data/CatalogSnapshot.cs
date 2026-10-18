using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FitMatrix.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FitMatrix.Api.Data;

public sealed record CatalogSnapshotSource(string Provider, string Url, string? License, DateTimeOffset LastCheckedAt);

public sealed record CatalogSnapshotProduct(
    string BrandSlug,
    string Brand,
    string Category,
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
    bool IsVerified,
    string DataQuality,
    IReadOnlyList<CatalogSnapshotSource> Sources);

public sealed record CatalogSnapshotDocument(
    string Version,
    DateTimeOffset ExportedAt,
    int ProductCount,
    IReadOnlyList<CatalogSnapshotProduct> Products);

/// <summary>
/// Ships the harvested catalog with the repository so a fresh clone starts with every verified product.
/// Only identity, physical measurements and source links are exported — not the manufacturers' page text.
/// Each snapshot version is applied once; products a user deleted afterwards are not brought back, and
/// products that already exist locally (same GTIN, brand + MPN or brand + model) are left untouched.
/// </summary>
public static class CatalogSnapshot
{
    public const string MarkerPrefix = "catalog-snapshot|";
    public static readonly string DefaultPath = Path.Combine(AppContext.BaseDirectory, "Data", "Catalog", "catalog-snapshot.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<CatalogSnapshotDocument> ExportAsync(FitMatrixDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var products = await dbContext.EquipmentProducts
            .AsNoTracking()
            .Include(product => product.Brand)
            .Include(product => product.Category)
            .Include(product => product.Sources)
            .Where(product => product.DataQuality != "template" && product.Sources.Any())
            .OrderBy(product => product.Brand.Slug)
            .ThenBy(product => product.DisplayName)
            .ThenBy(product => product.ModelName)
            .ToListAsync(cancellationToken);

        var exported = products.Select(product => new CatalogSnapshotProduct(
                product.Brand.Slug,
                product.Brand.Name,
                product.Category.Slug,
                product.DisplayName,
                product.ModelName,
                product.ManufacturerPartNumber,
                product.Gtin,
                product.WidthCm,
                product.HeightCm,
                product.DepthCm,
                product.FootprintWidthCm,
                product.FootprintDepthCm,
                product.WeightKg,
                product.EditorModel,
                product.DisplayColor,
                product.IsVerified,
                product.DataQuality,
                product.Sources
                    .OrderBy(source => source.SourceUrl, StringComparer.Ordinal)
                    .Select(source => new CatalogSnapshotSource(source.Provider, source.SourceUrl, source.LicenseName, source.LastCheckedAt))
                    .ToList()))
            .ToList();

        // The version follows the content, so re-exporting an unchanged catalog does not re-trigger imports.
        // Check timestamps are left out: re-verifying a source does not change what a new install receives.
        var content = exported.Select(product => product with
        {
            Sources = product.Sources.Select(source => source with { LastCheckedAt = default }).ToList()
        });
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content, JsonOptions)));
        return new CatalogSnapshotDocument(Convert.ToHexStringLower(contentHash)[..16], DateTimeOffset.UtcNow, exported.Count, exported);
    }

    public static string Serialize(CatalogSnapshotDocument document) => JsonSerializer.Serialize(document, JsonOptions);

    public static async Task<int> ApplyAsync(FitMatrixDbContext dbContext, string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return 0;
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<CatalogSnapshotDocument>(stream, JsonOptions, cancellationToken);
        return document is null ? 0 : await ApplyAsync(dbContext, document, cancellationToken);
    }

    public static async Task<int> ApplyAsync(FitMatrixDbContext dbContext, CatalogSnapshotDocument document, CancellationToken cancellationToken = default)
    {
        var marker = MarkerPrefix + document.Version;
        if (await dbContext.ProductLookupCaches.AnyAsync(entry => entry.NormalizedQuery == marker, cancellationToken)) return 0;

        var categories = await dbContext.EquipmentCategories
            .ToDictionaryAsync(category => category.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var brands = await dbContext.Brands
            .ToDictionaryAsync(brand => brand.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var existing = await dbContext.EquipmentProducts
            .AsNoTracking()
            .Select(product => new { product.Id, product.BrandId, product.Gtin, product.ManufacturerPartNumber, product.ModelName })
            .ToListAsync(cancellationToken);
        var existingIds = existing.Select(product => product.Id).ToHashSet();
        var existingGtins = existing.Where(product => product.Gtin != null).Select(product => product.Gtin!).ToHashSet();
        var existingKeys = existing
            .SelectMany(product => new[] { product.ManufacturerPartNumber, product.ModelName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => IdentityKey(product.BrandId, value!)))
            .ToHashSet(StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow;
        var inserted = 0;
        foreach (var item in document.Products)
        {
            if (!categories.TryGetValue(item.Category, out var category)) continue;
            if (!brands.TryGetValue(item.BrandSlug, out var brand))
            {
                brand = new Brand { Id = StableGuid($"brand:{item.BrandSlug}"), Name = item.Brand, Slug = item.BrandSlug };
                dbContext.Brands.Add(brand);
                brands.Add(item.BrandSlug, brand);
            }

            var identity = item.Gtin ?? item.ManufacturerPartNumber ?? item.ModelName;
            var productId = StableGuid($"snapshot:{item.BrandSlug}:{identity}");
            if (existingIds.Contains(productId) ||
                (item.Gtin is not null && existingGtins.Contains(item.Gtin)) ||
                existingKeys.Contains(IdentityKey(brand.Id, item.ManufacturerPartNumber ?? item.ModelName)) ||
                existingKeys.Contains(IdentityKey(brand.Id, item.ModelName)))
                continue;

            var product = new EquipmentProduct
            {
                Id = productId,
                BrandId = brand.Id,
                CategoryId = category.Id,
                DisplayName = item.DisplayName,
                ModelName = item.ModelName,
                ManufacturerPartNumber = item.ManufacturerPartNumber,
                Gtin = item.Gtin,
                WidthCm = item.WidthCm,
                HeightCm = item.HeightCm,
                DepthCm = item.DepthCm,
                FootprintWidthCm = item.FootprintWidthCm,
                FootprintDepthCm = item.FootprintDepthCm,
                WeightKg = item.WeightKg,
                EditorModel = item.EditorModel,
                DisplayColor = item.DisplayColor,
                IsVerified = item.IsVerified,
                DataQuality = item.DataQuality,
                CreatedAt = now,
                UpdatedAt = now
            };
            foreach (var source in item.Sources)
                product.Sources.Add(new ProductSource
                {
                    Id = StableGuid($"snapshot-source:{productId}:{source.Url}"),
                    ProductId = productId,
                    Provider = source.Provider,
                    SourceUrl = source.Url,
                    LicenseName = source.License,
                    LastCheckedAt = source.LastCheckedAt
                });

            dbContext.EquipmentProducts.Add(product);
            existingIds.Add(productId);
            existingKeys.Add(IdentityKey(brand.Id, item.ManufacturerPartNumber ?? item.ModelName));
            if (item.Gtin is not null) existingGtins.Add(item.Gtin);
            inserted++;
        }

        dbContext.ProductLookupCaches.Add(new ProductLookupCache
        {
            Id = Guid.NewGuid(),
            NormalizedQuery = marker,
            ResponseJson = JsonSerializer.Serialize(new { document.ExportedAt, document.ProductCount, Inserted = inserted }),
            CreatedAt = now,
            ExpiresAt = DateTimeOffset.MaxValue
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    private static Guid StableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"fitmatrix:{value.ToLowerInvariant()}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string IdentityKey(Guid brandId, string value) =>
        $"{brandId:N}:{new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray())}";
}
