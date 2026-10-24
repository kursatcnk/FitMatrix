using System.Text.RegularExpressions;
using FitMatrix.Api.Data;
using FitMatrix.Api.DTOs;
using FitMatrix.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FitMatrix.Api.Services;

public sealed record DiscoveryCatalogWriteResult(EquipmentProduct? Product, string? Error, bool Created);

// Persists a product found by the discovery pipeline. Shared by the editor's
// "add from discovery" endpoint and the bulk catalog harvester.
public sealed class DiscoveryCatalogWriter(FitMatrixDbContext dbContext, CatalogImportService importService)
{
    public async Task<DiscoveryCatalogWriteResult> WriteAsync(
        DiscoveryCatalogRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null) return new DiscoveryCatalogWriteResult(null, validation, false);

        var normalizedGtin = string.IsNullOrWhiteSpace(request.Gtin)
            ? null
            : new string(request.Gtin.Where(char.IsDigit).ToArray());
        // Without a GTIN or MPN the import cannot de-duplicate, so the model name becomes the part number.
        var normalizedMpn = string.IsNullOrWhiteSpace(request.ManufacturerPartNumber)
            ? normalizedGtin is null ? request.ModelName.Trim() : null
            : request.ManufacturerPartNumber.Trim();
        var brandName = request.Brand.Trim();

        // Sources disagree on part numbers ("3S" vs "MX Master 3S"); the same brand and product name is the
        // same product, so reuse its identity and update it instead of creating a twin.
        var productName = request.ProductName.Trim();
        var sameName = await dbContext.EquipmentProducts
            .AsNoTracking()
            .Where(item => item.Brand.Name == brandName && item.DisplayName == productName)
            .Select(item => new { item.Gtin, item.ManufacturerPartNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (sameName is not null && (sameName.Gtin is not null || sameName.ManufacturerPartNumber is not null))
        {
            normalizedGtin = sameName.Gtin;
            normalizedMpn = sameName.ManufacturerPartNumber;
        }
        var (width, height, depth) = ProductTaxonomy.OrientFlatDevice(request.EditorModel.Trim(), request.WidthCm, request.HeightCm, request.DepthCm);
        var reoriented = width != request.WidthCm || depth != request.DepthCm;
        var footprintWidth = reoriented ? width : request.FootprintWidthCm;
        var footprintDepth = reoriented ? depth : request.FootprintDepthCm;

        var isVerified = request.MeasurementStatus is "official-structured" or "source-structured";
        var importRequest = new CatalogImportRequest(
            request.Provider.Trim(),
            isVerified ? "Kaynak sayfası ürün verisi" : "Kullanıcı doğrulaması gerekli",
            [new CatalogImportItemDto(
                brandName,
                request.CategorySlug.Trim(),
                request.ProductName.Trim(),
                request.ModelName.Trim(),
                normalizedMpn,
                sameName?.Gtin ?? request.Gtin,
                width,
                height,
                depth,
                footprintWidth,
                footprintDepth,
                request.WeightKg,
                request.EditorModel.Trim(),
                request.DisplayColor.Trim(),
                null,
                request.SourceUrl.Trim(),
                sameName?.Gtin ?? request.Gtin ?? normalizedMpn,
                isVerified,
                null)]);

        var result = await importService.ImportAsync(importRequest, cancellationToken);
        if (result.Rejected > 0)
            return new DiscoveryCatalogWriteResult(null, result.Errors.FirstOrDefault()?.Message ?? "Ürün kataloğa alınamadı.", false);

        var product = await dbContext.EquipmentProducts
            .Include(item => item.Brand)
            .Include(item => item.Category)
            .Include(item => item.Specifications)
            .Where(item =>
                (normalizedGtin != null && item.Gtin == normalizedGtin) ||
                (normalizedGtin == null && normalizedMpn != null && item.ManufacturerPartNumber == normalizedMpn && item.Brand.Name == brandName))
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
            return new DiscoveryCatalogWriteResult(null, "Ürün kaydedildi ancak katalog kaydı tekrar okunamadı.", false);

        if (request.Specifications is { Count: > 0 })
        {
            UpsertSpecifications(product, request.Specifications, request.SourceUrl);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new DiscoveryCatalogWriteResult(product, null, result.Created > 0);
    }

    public static string? Validate(DiscoveryCatalogRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.ProductName) ||
            string.IsNullOrWhiteSpace(request.Brand) || string.IsNullOrWhiteSpace(request.ModelName))
            return "Sağlayıcı, ürün, marka ve model alanları zorunludur.";
        if (request.Provider.Length > 120 || request.ProductName.Length > 220 || request.Brand.Length > 120 ||
            request.ModelName.Length > 160 || request.ManufacturerPartNumber?.Length > 100 || request.Gtin?.Length > 20)
            return "Ürün kimliği alanlarından biri desteklenen uzunluğu aşıyor.";
        if (request.WidthCm <= 0 || request.HeightCm <= 0 || request.DepthCm <= 0)
            return "Ürünün genişlik, yükseklik ve derinlik ölçüleri bulunmadan kataloğa alınamaz.";
        if (request.WidthCm > 1000 || request.HeightCm > 1000 || request.DepthCm > 1000 || request.WeightKg is < 0 or > 10000)
            return "Ürün ölçüsü veya ağırlığı desteklenen aralığın dışında.";
        if (request.FootprintWidthCm <= 0 || request.FootprintDepthCm <= 0 ||
            request.FootprintWidthCm > request.WidthCm || request.FootprintDepthCm > request.DepthCm)
            return "Temas alanı pozitif olmalı ve ürün gövde ölçülerini aşmamalıdır.";
        if (!ProductTaxonomy.SupportedCategories.Contains(request.CategorySlug.Trim()) ||
            !ProductTaxonomy.SupportedEditorModels.Contains(request.EditorModel.Trim()))
            return "Kategori veya 3D model türü desteklenmiyor.";
        if (!Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme is not ("http" or "https"))
            return "Geçerli bir HTTP/HTTPS ürün kaynağı zorunludur.";
        if (request.SourceUrl.Length > 1000)
            return "Ürün kaynağı adresi en fazla 1000 karakter olabilir.";
        if (!Regex.IsMatch(request.DisplayColor, "^#[0-9a-fA-F]{6}$"))
            return "Görüntü rengi #RRGGBB biçiminde olmalıdır.";
        return null;
    }

    private void UpsertSpecifications(
        EquipmentProduct product,
        IReadOnlyList<DiscoverySpecificationDto> specifications,
        string sourceUrl)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var incoming in specifications
                     .Where(item => !string.IsNullOrWhiteSpace(item.Group) && !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Value))
                     .Take(250))
        {
            var group = incoming.Group.Trim()[..Math.Min(incoming.Group.Trim().Length, 120)];
            var name = incoming.Name.Trim()[..Math.Min(incoming.Name.Trim().Length, 180)];
            var value = incoming.Value.Trim()[..Math.Min(incoming.Value.Trim().Length, 2000)];
            var unit = string.IsNullOrWhiteSpace(incoming.Unit) ? null : incoming.Unit.Trim()[..Math.Min(incoming.Unit.Trim().Length, 60)];
            var key = string.Join('|', group, name).ToLowerInvariant();
            var existing = product.Specifications.FirstOrDefault(item => item.NormalizedKey == key);
            if (existing is null)
            {
                var specification = new ProductSpecification
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Group = group,
                    Name = name,
                    NormalizedKey = key,
                    Value = value,
                    Unit = unit,
                    SourceUrl = sourceUrl,
                    LastCheckedAt = now
                };
                product.Specifications.Add(specification);
                dbContext.ProductSpecifications.Add(specification);
            }
            else
            {
                existing.Group = group;
                existing.Name = name;
                existing.Value = value;
                existing.Unit = unit;
                existing.SourceUrl = sourceUrl;
                existing.LastCheckedAt = now;
            }
        }
    }
}
