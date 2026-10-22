using FitMatrix.Api.Controllers;
using FitMatrix.Api.Data;
using FitMatrix.Api.DTOs;
using FitMatrix.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FitMatrix.Api.Tests;

public sealed class CatalogAndProjectControllerTests
{
    [Fact]
    public async Task CreateProject_AcceptsExpandedFurnitureModel()
    {
        await using var dbContext = CreateContext();
        var controller = new ProjectsController(dbContext);
        var request = new ProjectUpsertRequest(
            "Mobilya yerleşimi",
            160,
            80,
            75,
            [new ProjectItemRequest(null, "IKEA MICKE", "furniture", "desk", "#756752", 73, 75, 50, 73, 50, 0, 0, 0)]);

        var response = await controller.CreateProject(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var project = Assert.IsType<ProjectDto>(created.Value);
        Assert.Single(project.Items);
        Assert.Equal("furniture", project.Items[0].CategorySlug);
        Assert.Equal("desk", project.Items[0].EditorModel);
    }

    [Fact]
    public async Task CreateProject_PersistsMountedPortraitDisplay()
    {
        await using var dbContext = CreateContext();
        var controller = new ProjectsController(dbContext);
        var request = new ProjectUpsertRequest(
            "Üç monitör düzeni",
            128,
            58,
            72,
            [new ProjectItemRequest(null, "MSI PRO MP225", "display", "monitor", "#242a2e", 49.2m, 28.3m, 4m, 49.2m, 4m, 44.3m, -24m, 0, 10m, "vesa", "portrait", 0)]);

        var response = await controller.CreateProject(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var project = Assert.IsType<ProjectDto>(created.Value);
        var display = Assert.Single(project.Items);
        Assert.Equal(10m, display.ElevationCm);
        Assert.Equal("vesa", display.MountMode);
        Assert.Equal("portrait", display.ScreenOrientation);
    }

    [Fact]
    public async Task GetProducts_ReturnsStableNonOverlappingPages()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.EnsureCreatedAsync();
        var controller = new CatalogController(dbContext, new CatalogImportService(dbContext));

        var firstResponse = await controller.GetProducts(null, null, 0, 5, CancellationToken.None);
        var secondResponse = await controller.GetProducts(null, null, 5, 5, CancellationToken.None);
        var first = Assert.IsType<OkObjectResult>(firstResponse.Result).Value as IReadOnlyList<CatalogProductDto>;
        var second = Assert.IsType<OkObjectResult>(secondResponse.Result).Value as IReadOnlyList<CatalogProductDto>;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(5, first.Count);
        Assert.Equal(5, second.Count);
        Assert.Empty(first.Select(item => item.Id).Intersect(second.Select(item => item.Id)));
    }

    [Fact]
    public async Task DeleteProduct_RemovesOnlyTheSelectedCatalogRecord()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.EnsureCreatedAsync();
        var product = await dbContext.EquipmentProducts.FirstAsync();
        var originalCount = await dbContext.EquipmentProducts.CountAsync();
        var controller = new CatalogController(dbContext, new CatalogImportService(dbContext));

        var response = await controller.DeleteProduct(product.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
        Assert.Equal(originalCount - 1, await dbContext.EquipmentProducts.CountAsync());
        Assert.False(await dbContext.EquipmentProducts.AnyAsync(item => item.Id == product.Id));
    }

    [Fact]
    public async Task ExpandedCatalogSeed_AddsBroadBrandAndModelBaselineIdempotently()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.EnsureCreatedAsync();

        await ExpandedCatalogSeed.SeedAsync(dbContext);
        var productCount = await dbContext.EquipmentProducts.CountAsync();
        var brandCount = await dbContext.Brands.CountAsync();

        await ExpandedCatalogSeed.SeedAsync(dbContext);

        Assert.True(productCount >= 55);
        Assert.True(brandCount >= 25);
        Assert.Equal(productCount, await dbContext.EquipmentProducts.CountAsync());
        Assert.Equal(brandCount, await dbContext.Brands.CountAsync());
        Assert.True(await dbContext.EquipmentProducts.AnyAsync(product => product.ModelName == "MX Keys S"));
        Assert.True(await dbContext.EquipmentProducts.AnyAsync(product => product.ModelName == "DeathAdder V3 Pro"));
        Assert.True(await dbContext.EquipmentProducts.AnyAsync(product => product.ModelName == "MAG 274QF"));
        Assert.True(await dbContext.EquipmentProducts.AnyAsync(product => product.ModelName == "A31 PLUS TG ARGB"));
    }

    [Fact]
    public async Task ExpandedCatalogSeed_DoesNotDuplicateAnExistingBrandAndMpn()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.EnsureCreatedAsync();
        var display = await dbContext.EquipmentCategories.SingleAsync(category => category.Slug == "display");
        var aoc = new FitMatrix.Api.Models.Brand
        {
            Id = Guid.NewGuid(),
            Name = "AOC",
            Slug = "aoc"
        };
        dbContext.Brands.Add(aoc);
        dbContext.EquipmentProducts.Add(new FitMatrix.Api.Models.EquipmentProduct
        {
            Id = Guid.NewGuid(),
            BrandId = aoc.Id,
            CategoryId = display.Id,
            DisplayName = "AOC Q27G3XMN",
            ModelName = "Q27G3XMN",
            ManufacturerPartNumber = "Q27G3XMN",
            WidthCm = 61.45m,
            HeightCm = 51.96m,
            DepthCm = 30.16m,
            FootprintWidthCm = 61.45m,
            FootprintDepthCm = 30.16m,
            EditorModel = "monitor",
            DisplayColor = "#252a2e",
            DataQuality = "source-verified",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        await ExpandedCatalogSeed.SeedAsync(dbContext);

        Assert.Equal(1, await dbContext.EquipmentProducts.CountAsync(product =>
            product.BrandId == aoc.Id && product.ManufacturerPartNumber == "Q27G3XMN"));
        Assert.True(await dbContext.EquipmentProducts.AnyAsync(product => product.ModelName == "MX Keys S"));
    }

    private static FitMatrixDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FitMatrixDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FitMatrixDbContext(options);
    }
}
