using FitMatrix.Api.Data;
using FitMatrix.Api.DTOs;
using FitMatrix.Api.Models;
using FitMatrix.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitMatrix.Api.Controllers;

[ApiController]
[Route("api/catalog")]
public class CatalogController(FitMatrixDbContext dbContext, CatalogImportService importService) : ControllerBase
{
    private static readonly HashSet<string> SupportedCategories = ProductTaxonomy.SupportedCategories;
    private static readonly HashSet<string> SupportedEditorModels = ProductTaxonomy.SupportedEditorModels;

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetCategories(CancellationToken cancellationToken)
    {
        var categories = await dbContext.EquipmentCategories
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new CategoryDto(x.Id, x.Name, x.Slug, x.EditorModel, x.Products.Count))
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpGet("products")]
    public async Task<ActionResult<IReadOnlyList<CatalogProductDto>>> GetProducts(
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.EquipmentProducts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category.Slug == category);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.DisplayName.Contains(term) || x.ModelName.Contains(term) || x.Brand.Name.Contains(term));
        }

        var productEntities = await query
            .Include(x => x.Brand)
            .Include(x => x.Category)
            .OrderBy(x => x.Brand.Name)
            .ThenBy(x => x.DisplayName)
            .ThenBy(x => x.Id)
            .Skip(Math.Clamp(offset, 0, 1_000_000))
            .Take(Math.Clamp(limit, 1, 250))
            .ToListAsync(cancellationToken);

        return Ok(productEntities.Select(MapProduct).ToList());
    }

    [HttpGet("products/{id:guid}")]
    public async Task<ActionResult<CatalogProductDetailDto>> GetProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await dbContext.EquipmentProducts
            .AsNoTracking()
            .Include(x => x.Brand)
            .Include(x => x.Category)
            .Include(x => x.AttributeValues).ThenInclude(x => x.AttributeDefinition)
            .Include(x => x.Specifications)
            .Include(x => x.Sources)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (product is null) return NotFound();

        return Ok(new CatalogProductDetailDto(
            MapProduct(product),
            product.AttributeValues.OrderBy(value => value.AttributeDefinition.Name).Select(value => new ProductAttributeDto(
                value.AttributeDefinition.Code,
                value.AttributeDefinition.Name,
                value.AttributeDefinition.ValueType,
                value.AttributeDefinition.Unit,
                value.NumericValue,
                value.TextValue,
                value.BooleanValue)).ToList(),
            product.Specifications.OrderBy(spec => spec.Group).ThenBy(spec => spec.Name).Select(spec => new ProductSpecificationDto(
                spec.Group,
                spec.Name,
                spec.Value,
                spec.Unit,
                spec.SourceUrl,
                spec.LastCheckedAt)).ToList(),
            product.Sources.OrderByDescending(source => source.LastCheckedAt).Select(source => new ProductSourceDto(
                source.Provider,
                source.SourceUrl,
                source.ExternalId,
                source.LicenseName,
                source.LastCheckedAt)).ToList()));
    }

    [HttpDelete("products/{id:guid}")]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await dbContext.EquipmentProducts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null) return NotFound();
        dbContext.EquipmentProducts.Remove(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("products/from-discovery")]
    public async Task<ActionResult<CatalogProductDto>> ImportDiscoveredProduct(
        DiscoveryCatalogRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateDiscoveryImport(request);
        if (validation is not null) return BadRequest(new { message = validation });

        var isVerified = request.MeasurementStatus is "official-structured" or "source-structured";
        var importRequest = new CatalogImportRequest(
            request.Provider.Trim(),
            isVerified ? "Kaynak sayfası ürün verisi" : "Kullanıcı doğrulaması gerekli",
            [new CatalogImportItemDto(
                request.Brand.Trim(),
                request.CategorySlug.Trim(),
                request.ProductName.Trim(),
                request.ModelName.Trim(),
                string.IsNullOrWhiteSpace(request.ManufacturerPartNumber) ? null : request.ManufacturerPartNumber.Trim(),
                request.Gtin,
                request.WidthCm,
                request.HeightCm,
                request.DepthCm,
                request.FootprintWidthCm,
                request.FootprintDepthCm,
                request.WeightKg,
                request.EditorModel.Trim(),
                request.DisplayColor.Trim(),
                null,
                request.SourceUrl.Trim(),
                request.Gtin ?? request.ManufacturerPartNumber,
                isVerified,
                null)]);

        var result = await importService.ImportAsync(importRequest, cancellationToken);
        if (result.Rejected > 0)
            return BadRequest(new { message = result.Errors.FirstOrDefault()?.Message ?? "Ürün kataloğa alınamadı." });

        var normalizedGtin = string.IsNullOrWhiteSpace(request.Gtin)
            ? null
            : new string(request.Gtin.Where(char.IsDigit).ToArray());
        var normalizedMpn = string.IsNullOrWhiteSpace(request.ManufacturerPartNumber)
            ? null
            : request.ManufacturerPartNumber.Trim();
        var product = await dbContext.EquipmentProducts
            .Include(item => item.Brand)
            .Include(item => item.Category)
            .Include(item => item.Specifications)
            .Where(item =>
                (normalizedGtin != null && item.Gtin == normalizedGtin) ||
                (normalizedGtin == null && normalizedMpn != null && item.ManufacturerPartNumber == normalizedMpn && item.Brand.Name == request.Brand.Trim()))
            .SingleOrDefaultAsync(cancellationToken);

        if (product is not null && request.Specifications is { Count: > 0 })
        {
            UpsertSpecifications(product, request.Specifications, request.SourceUrl);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return product is null
            ? Problem("Ürün kaydedildi ancak katalog kaydı tekrar okunamadı.", statusCode: StatusCodes.Status500InternalServerError)
            : Ok(MapProduct(product));
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
            var key = NormalizeSpecificationKey(group, name);
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

    private static string NormalizeSpecificationKey(string group, string name) =>
        string.Join('|', group, name).ToLowerInvariant();

    private static string? ValidateDiscoveryImport(DiscoveryCatalogRequestDto request)
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
        if (!SupportedCategories.Contains(request.CategorySlug.Trim()) || !SupportedEditorModels.Contains(request.EditorModel.Trim()))
            return "Kategori veya 3D model türü desteklenmiyor.";
        if (!Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme is not ("http" or "https"))
            return "Geçerli bir HTTP/HTTPS ürün kaynağı zorunludur.";
        if (request.SourceUrl.Length > 1000)
            return "Ürün kaynağı adresi en fazla 1000 karakter olabilir.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(request.DisplayColor, "^#[0-9a-fA-F]{6}$"))
            return "Görüntü rengi #RRGGBB biçiminde olmalıdır.";
        return null;
    }

    private static CatalogProductDto MapProduct(EquipmentProduct product) => new(
        product.Id,
        product.Brand.Name,
        product.Category.Name,
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
        product.ImageUrl,
        product.IsVerified,
        product.DataQuality);
}
