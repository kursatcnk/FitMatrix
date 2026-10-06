using FitMatrix.Api.DTOs;
using FitMatrix.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace FitMatrix.Api.Controllers;

[ApiController]
[Route("api/product-discovery")]
[EnableRateLimiting("product-search")]
public sealed class ProductDiscoveryController(ProductDiscoveryService discoveryService) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<ProductDiscoveryResponseDto>> Search(
        [FromQuery] string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return BadRequest(new { message = "En az iki karakterlik ürün adı, model kodu veya GTIN girin." });
        if (query.Length > 2048)
            return BadRequest(new { message = "Ürün adı veya bağlantı en fazla 2048 karakter olabilir." });

        return Ok(await discoveryService.SearchAsync(query, cancellationToken));
    }
}
