using FitMatrix.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitMatrix.Api.Controllers;

[ApiController]
[Route("api/system")]
public class SystemController(FitMatrixDbContext dbContext) : ControllerBase
{
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        try
        {
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { service = "FitMatrix API", status = "unhealthy", database = "unavailable", utcTime = DateTimeOffset.UtcNow });

            var productCount = await dbContext.EquipmentProducts.CountAsync(cancellationToken);
            var projectCount = await dbContext.DeskProjects.CountAsync(cancellationToken);
            return Ok(new
            {
                service = "FitMatrix API",
                status = "healthy",
                database = "connected",
                version = "1.0.0",
                productCount,
                projectCount,
                utcTime = DateTimeOffset.UtcNow
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                service = "FitMatrix API",
                status = "unhealthy",
                database = "unavailable",
                utcTime = DateTimeOffset.UtcNow
            });
        }
    }
}
