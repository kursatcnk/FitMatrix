using System.Globalization;
using System.Text;
using FitMatrix.Api.Data;
using FitMatrix.Api.DTOs;
using FitMatrix.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace FitMatrix.Api.Services;

public sealed class CatalogImportService(FitMatrixDbContext dbContext)
{
    private static readonly HashSet<int> ValidGtinLengths = [8, 12, 13, 14];

    public async Task<CatalogImportResultDto> ImportAsync(
        CatalogImportRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<CatalogImportErrorDto>();
        var created = 0;
        var updated = 0;

        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return new CatalogImportResultDto(
                request.Products?.Count ?? 0,
                0,
                0,
                request.Products?.Count ?? 0,
                [new CatalogImportErrorDto(0, "Toplu içe aktarma", "Provider alanı zorunludur.")]);
        }
        if (request.Provider.Trim().Length > 100 || request.LicenseName?.Trim().Length > 160)
        {
            return new CatalogImportResultDto(
                request.Products?.Count ?? 0,
                0,
                0,
                request.Products?.Count ?? 0,
                [new CatalogImportErrorDto(0, "Toplu içe aktarma", "Provider en fazla 100, lisans adı en fazla 160 karakter olabilir.")]);
        }

        if (request.Products is null || request.Products.Count == 0)
        {
            return new CatalogImportResultDto(0, 0, 0, 0, []);
        }

        var categories = await dbContext.EquipmentCategories
            .Include(category => category.AttributeDefinitions)
            .ToDictionaryAsync(category => category.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var brands = await dbContext.Brands
            .ToDictionaryAsync(brand => brand.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        for (var index = 0; index < request.Products.Count; index++)
        {
            var item = request.Products[index];
            var label = string.IsNullOrWhiteSpace(item.DisplayName) ? $"Satır {index + 1}" : item.DisplayName.Trim();

            try
            {
                Validate(item);

                if (!categories.TryGetValue(item.CategorySlug.Trim(), out var category))
                {
                    throw new InvalidOperationException($"Bilinmeyen kategori: {item.CategorySlug}");
                }

                ValidateAttributes(category, item.Attributes);

                var brandSlug = Slugify(item.BrandName);
                if (brandSlug.Length == 0) throw new ArgumentException("Marka adı en az bir harf veya rakam içermelidir.");
                if (!brands.TryGetValue(brandSlug, out var brand))
                {
                    brand = new Brand
                    {
                        Id = Guid.NewGuid(),
                        Name = item.BrandName.Trim(),
                        Slug = brandSlug
                    };
                    brands.Add(brandSlug, brand);
                    dbContext.Brands.Add(brand);
                }

                var gtin = NormalizeGtin(item.Gtin);
                var mpn = NormalizeOptional(item.ManufacturerPartNumber);
                var product = await FindProductAsync(brand.Id, gtin, mpn, cancellationToken);
                var isNew = product is null;

                product ??= new EquipmentProduct
                {
                    Id = Guid.NewGuid(),
                    BrandId = brand.Id,
                    Brand = brand,
                    CategoryId = category.Id,
                    Category = category,
                    DisplayName = label,
                    ModelName = item.ModelName.Trim(),
                    EditorModel = category.EditorModel,
                    DisplayColor = "#22272b",
                    DataQuality = "imported",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                ApplyProductValues(product, item, category, gtin, mpn);
                UpsertSource(product, request, item);
                UpsertAttributes(product, category, item.Attributes);

                if (isNew)
                {
                    dbContext.EquipmentProducts.Add(product);
                    created++;
                }
                else
                {
                    updated++;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                errors.Add(new CatalogImportErrorDto(index + 1, label, exception.Message));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CatalogImportResultDto(request.Products.Count, created, updated, errors.Count, errors);
    }

    private async Task<EquipmentProduct?> FindProductAsync(
        Guid brandId,
        string? gtin,
        string? mpn,
        CancellationToken cancellationToken)
    {
        IQueryable<EquipmentProduct> query = dbContext.EquipmentProducts
            .Include(product => product.Sources)
            .Include(product => product.AttributeValues)
            .AsSplitQuery();

        if (gtin is not null)
        {
            var byGtin = await query.SingleOrDefaultAsync(product => product.Gtin == gtin, cancellationToken);
            if (byGtin is not null) return byGtin;
        }

        return mpn is null
            ? null
            : await query.SingleOrDefaultAsync(
                product => product.BrandId == brandId && product.ManufacturerPartNumber == mpn,
                cancellationToken);
    }

    private static void ApplyProductValues(
        EquipmentProduct product,
        CatalogImportItemDto item,
        EquipmentCategory category,
        string? gtin,
        string? mpn)
    {
        product.CategoryId = category.Id;
        product.Category = category;
        product.DisplayName = item.DisplayName.Trim();
        product.ModelName = item.ModelName.Trim();
        product.ManufacturerPartNumber = mpn;
        product.Gtin = gtin;
        product.WidthCm = item.WidthCm;
        product.HeightCm = item.HeightCm;
        product.DepthCm = item.DepthCm;
        product.FootprintWidthCm = item.FootprintWidthCm ?? item.WidthCm;
        product.FootprintDepthCm = item.FootprintDepthCm ?? item.DepthCm;
        product.WeightKg = item.WeightKg;
        product.EditorModel = NormalizeOptional(item.EditorModel) ?? category.EditorModel;
        product.DisplayColor = NormalizeOptional(item.DisplayColor) ?? "#22272b";
        product.ImageUrl = NormalizeOptional(item.ImageUrl);
        product.IsVerified = item.IsVerified;
        product.DataQuality = item.IsVerified ? "source-verified" : "imported";
        product.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void UpsertSource(
        EquipmentProduct product,
        CatalogImportRequest request,
        CatalogImportItemDto item)
    {
        var provider = request.Provider.Trim();
        var externalId = NormalizeOptional(item.ExternalId);
        var sourceUrl = item.SourceUrl.Trim();
        var source = product.Sources.FirstOrDefault(existing =>
            existing.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
            ((externalId is not null && existing.ExternalId == externalId) || existing.SourceUrl == sourceUrl));

        if (source is null)
        {
            product.Sources.Add(new ProductSource
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                ExternalId = externalId,
                SourceUrl = sourceUrl,
                LicenseName = NormalizeOptional(request.LicenseName),
                LastCheckedAt = DateTimeOffset.UtcNow
            });
            return;
        }

        source.SourceUrl = sourceUrl;
        source.ExternalId = externalId;
        source.LicenseName = NormalizeOptional(request.LicenseName);
        source.LastCheckedAt = DateTimeOffset.UtcNow;
    }

    private static void UpsertAttributes(
        EquipmentProduct product,
        EquipmentCategory category,
        IReadOnlyList<CatalogImportAttributeDto>? attributes)
    {
        if (attributes is null) return;

        var definitions = category.AttributeDefinitions.ToDictionary(
            definition => definition.Code,
            StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in attributes)
        {
            if (!definitions.TryGetValue(attribute.Code.Trim(), out var definition)) continue;

            var existing = product.AttributeValues.FirstOrDefault(value => value.AttributeDefinitionId == definition.Id);
            if (existing is null)
            {
                existing = new ProductAttributeValue
                {
                    ProductId = product.Id,
                    AttributeDefinitionId = definition.Id,
                    AttributeDefinition = definition
                };
                product.AttributeValues.Add(existing);
            }

            existing.NumericValue = attribute.NumericValue;
            existing.TextValue = NormalizeOptional(attribute.TextValue);
            existing.BooleanValue = attribute.BooleanValue;
        }
    }

    private static void Validate(CatalogImportItemDto item)
    {
        if (string.IsNullOrWhiteSpace(item.BrandName)) throw new ArgumentException("Marka adı zorunludur.");
        if (string.IsNullOrWhiteSpace(item.CategorySlug)) throw new ArgumentException("Kategori zorunludur.");
        if (string.IsNullOrWhiteSpace(item.DisplayName)) throw new ArgumentException("Ürün adı zorunludur.");
        if (string.IsNullOrWhiteSpace(item.ModelName)) throw new ArgumentException("Model adı zorunludur.");
        if (item.BrandName.Trim().Length > 120 || item.CategorySlug.Trim().Length > 140 ||
            item.DisplayName.Trim().Length > 220 || item.ModelName.Trim().Length > 160 ||
            item.ManufacturerPartNumber?.Trim().Length > 100)
            throw new ArgumentException("Ürün kimliği alanlarından biri desteklenen uzunluğu aşıyor.");
        if (item.WidthCm <= 0 || item.HeightCm <= 0 || item.DepthCm <= 0)
            throw new ArgumentException("Genişlik, yükseklik ve derinlik sıfırdan büyük olmalıdır.");
        if (item.WidthCm > 1000 || item.HeightCm > 1000 || item.DepthCm > 1000)
            throw new ArgumentException("Ürün ölçüleri desteklenen 1000 cm sınırını aşıyor.");
        if (item.FootprintWidthCm <= 0 || item.FootprintDepthCm <= 0 ||
            item.FootprintWidthCm > item.WidthCm || item.FootprintDepthCm > item.DepthCm)
            throw new ArgumentException("Temas alanı pozitif olmalı ve ürün gövde ölçülerini aşmamalıdır.");
        if (item.WeightKg is <= 0 or > 10000) throw new ArgumentException("Ağırlık desteklenen aralıkta olmalıdır.");
        if (!string.IsNullOrWhiteSpace(item.EditorModel) && !ProductTaxonomy.SupportedEditorModels.Contains(item.EditorModel.Trim()))
            throw new ArgumentException("3D model türü desteklenmiyor.");
        if (!string.IsNullOrWhiteSpace(item.DisplayColor) && !Regex.IsMatch(item.DisplayColor.Trim(), "^#[0-9a-fA-F]{6}$"))
            throw new ArgumentException("Görüntü rengi #RRGGBB biçiminde olmalıdır.");
        if (string.IsNullOrWhiteSpace(item.SourceUrl) ||
            !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var sourceUri) ||
            (sourceUri.Scheme != Uri.UriSchemeHttp && sourceUri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Geçerli bir HTTP/HTTPS kaynak adresi zorunludur.");
        if (item.SourceUrl.Trim().Length > 1000 || item.ExternalId?.Trim().Length > 160)
            throw new ArgumentException("Kaynak adresi veya dış kimlik desteklenen uzunluğu aşıyor.");
        if (!string.IsNullOrWhiteSpace(item.ImageUrl) &&
            (!Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out var imageUri) || imageUri.Scheme is not ("http" or "https") || item.ImageUrl.Length > 1000))
            throw new ArgumentException("Ürün görseli geçerli bir HTTP/HTTPS adresi olmalıdır.");

        var gtin = NormalizeGtin(item.Gtin);
        if (gtin is null && string.IsNullOrWhiteSpace(item.ManufacturerPartNumber))
            throw new ArgumentException("Tekilleştirme için GTIN veya üretici parça numarası zorunludur.");
    }

    private static void ValidateAttributes(
        EquipmentCategory category,
        IReadOnlyList<CatalogImportAttributeDto>? attributes)
    {
        if (attributes is null) return;

        var definitions = category.AttributeDefinitions.ToDictionary(
            definition => definition.Code,
            StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Code) || !definitions.TryGetValue(attribute.Code.Trim(), out var definition))
                throw new InvalidOperationException($"{category.Name} kategorisinde tanımsız özellik: {attribute.Code}");

            var valueCount = (attribute.NumericValue.HasValue ? 1 : 0)
                + (string.IsNullOrWhiteSpace(attribute.TextValue) ? 0 : 1)
                + (attribute.BooleanValue.HasValue ? 1 : 0);
            if (valueCount != 1)
                throw new ArgumentException($"{definition.Name} için tam olarak bir değer verilmelidir.");

            var matchesType = definition.ValueType switch
            {
                "number" => attribute.NumericValue.HasValue,
                "boolean" => attribute.BooleanValue.HasValue,
                _ => !string.IsNullOrWhiteSpace(attribute.TextValue)
            };
            if (!matchesType) throw new ArgumentException($"{definition.Name} değeri {definition.ValueType} türünde olmalıdır.");
        }
    }

    private static string? NormalizeGtin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(char.IsDigit).ToArray());
        if (!ValidGtinLengths.Contains(normalized.Length))
            throw new ArgumentException("GTIN 8, 12, 13 veya 14 haneli olmalıdır.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var pendingSeparator = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0) builder.Append('-');
                builder.Append(character);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
