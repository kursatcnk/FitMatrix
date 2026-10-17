using FitMatrix.Api.Data;
using FitMatrix.Api.DTOs;
using FitMatrix.Api.Models;
using FitMatrix.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitMatrix.Api.Controllers;

[ApiController]
[Route("api/catalog")]
public class CatalogController(FitMatrixDbContext dbContext, DiscoveryCatalogWriter discoveryWriter) : ControllerBase
{
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
        var result = await discoveryWriter.WriteAsync(request, cancellationToken);
        return result.Product is null
            ? BadRequest(new { message = result.Error })
            : Ok(MapProduct(result.Product));
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
