using FitMatrix.Api.Data;
using FitMatrix.Api.DTOs;
using FitMatrix.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FitMatrix.Api.Tests;

public sealed class CatalogHarvestTests
{
    private static readonly ProductProviderStatusDto Provider = new("Test", true, "");

    private static ExternalProductCandidateDto Candidate(
        string status = "official-structured",
        decimal? width = 61.4m,
        string? sourceUrl = "https://www.aoc.com/monitors/q27g3xmn",
        string? mpn = "Q27G3XMN") => new(
        "Açık web",
        "AOC Q27G3XMN",
        "AOC",
        "Q27G3XMN",
        mpn,
        null,
        width,
        52m,
        30.2m,
        6.1m,
        sourceUrl,
        status,
        "",
        [new ExternalSpecificationDto("Panel", "Diyagonal", "27", "inch")],
        "display",
        "monitor",
        "#252a2e");

    private static ProductDiscoveryResponseDto Response(
        ExternalProductCandidateDto? external,
        params ProductDiscoveryResultDto[] local) =>
        new("AOC Q27G3XMN", local, external, external is null ? [] : [external], Provider);

    [Fact]
    public void Plan_BuildsImportForOfficialThreeAxisMeasurements()
    {
        var plan = CatalogHarvestPlanner.Plan(Response(Candidate()), requireVerified: true);

        Assert.Null(plan.Skip);
        Assert.NotNull(plan.Request);
        Assert.Equal("display", plan.Request.CategorySlug);
        Assert.Equal(61.4m, plan.Request.FootprintWidthCm);
        Assert.Single(plan.Request.Specifications!);
    }

    [Theory]
    [InlineData("provider-order")]
    [InlineData("identity-only")]
    public void Plan_RejectsUnverifiedMeasurementsByDefault(string status)
    {
        var plan = CatalogHarvestPlanner.Plan(Response(Candidate(status)), requireVerified: true);

        Assert.Equal(CatalogHarvestOutcome.NoVerifiedMatch, plan.Skip);
        Assert.Null(plan.Request);
    }

    [Fact]
    public void Plan_AllowsProviderMeasurementsWhenVerificationIsRelaxed()
    {
        var plan = CatalogHarvestPlanner.Plan(Response(Candidate("provider-order")), requireVerified: false);

        Assert.NotNull(plan.Request);
    }

    [Fact]
    public void Plan_NeverStoresMissingDimensionsOrSources()
    {
        Assert.Null(CatalogHarvestPlanner.Plan(Response(Candidate(width: null)), false).Request);
        Assert.Null(CatalogHarvestPlanner.Plan(Response(Candidate(sourceUrl: null)), false).Request);
    }

    [Fact]
    public void Plan_ReportsVerifiedLocalProductAsAlreadyInCatalog()
    {
        var local = new ProductDiscoveryResultDto(Guid.NewGuid(), "AOC", "AOC Q27G3XMN", "Q27G3XMN", "Q27G3XMN", null,
            61.4m, 52m, 30.2m, 61.4m, 30.2m, 6.1m, "source-verified", true, 1, 99);

        var plan = CatalogHarvestPlanner.Plan(Response(null, local), true);

        Assert.Equal(CatalogHarvestOutcome.AlreadyInCatalog, plan.Skip);
    }

    [Fact]
    public void Plan_TreatsAnExactDisplayNameMatchAsAlreadyInCatalog()
    {
        var local = new ProductDiscoveryResultDto(Guid.NewGuid(), "Razer", "Razer Viper V3 Pro", "Viper V3 Pro", null, null,
            12.7m, 3.9m, 6.4m, 12.7m, 6.4m, null, "imported", false, 1, 92);

        Assert.Equal(CatalogHarvestOutcome.AlreadyInCatalog, CatalogHarvestPlanner.Plan(Response(null, local), true).Skip);
    }

    [Fact]
    public void ParseQueryList_SkipsCommentsBlankLinesAndDuplicates()
    {
        var queries = CatalogHarvestPlanner.ParseQueryList(["# Monitörler", "", "Dell U2723QE", "dell u2723qe", "  LG 27GP850-B  ", "ab"]);

        Assert.Equal(["Dell U2723QE", "LG 27GP850-B"], queries.Select(query => query.Query));
    }

    [Fact]
    public void ParseQueryList_AppliesSectionHintsAndIgnoresUnknownOnes()
    {
        var queries = CatalogHarvestPlanner.ParseQueryList(["[computer:tower]", "NZXT H5 Flow", "[nope:thing]", "Mystery Gadget"]);

        Assert.Equal("tower", queries[0].Hint?.EditorModel);
        Assert.Null(queries[1].Hint);
    }

    [Theory]
    [InlineData("keyboard", 43.39, 13.16, 2.61, 43.39, 2.61, 13.16)]
    [InlineData("mouse", 8.43, 12.49, 5.10, 8.43, 5.10, 12.49)]
    [InlineData("mouse", 12.49, 8.43, 5.10, 8.43, 5.10, 12.49)]
    [InlineData("monitor", 61.4, 52, 30.2, 61.4, 52, 30.2)]
    public void OrientFlatDevice_PutsTheThinnestSideVertical(string model, double w, double h, double d, double ew, double eh, double ed)
    {
        var (width, height, depth) = ProductTaxonomy.OrientFlatDevice(model, (decimal)w, (decimal)h, (decimal)d);

        Assert.Equal(((decimal)ew, (decimal)eh, (decimal)ed), (width, height, depth));
    }

    [Fact]
    public void Plan_RejectsSizesImpossibleForTheProductFamily()
    {
        var headset = Candidate() with { WidthCm = 26.2m, HeightCm = 177.5m, DepthCm = 8.72m };

        var plan = CatalogHarvestPlanner.Plan(Response(headset), true, ProductTaxonomy.FromHint("audio", "headphone"));

        Assert.Equal(CatalogHarvestOutcome.NoVerifiedMatch, plan.Skip);
    }

    [Fact]
    public void Plan_PrefersTheCuratedListHintOverPageClassification()
    {
        var hint = ProductTaxonomy.FromHint("computer", "tower");

        var plan = CatalogHarvestPlanner.Plan(Response(Candidate()), true, hint);

        Assert.Equal("computer", plan.Request!.CategorySlug);
        Assert.Equal("tower", plan.Request.EditorModel);
    }

    [Fact]
    public async Task Writer_ImportsOnceAndUpdatesOnRepeat()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.EnsureCreatedAsync();
        var writer = new DiscoveryCatalogWriter(dbContext, new CatalogImportService(dbContext));
        var request = CatalogHarvestPlanner.Plan(Response(Candidate()), true).Request!;

        var first = await writer.WriteAsync(request, CancellationToken.None);
        var second = await writer.WriteAsync(request, CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.NotNull(second.Product);
        Assert.Equal(1, await dbContext.EquipmentProducts.CountAsync(product => product.ManufacturerPartNumber == "Q27G3XMN"));
        Assert.True(second.Product.IsVerified);
    }

    [Fact]
    public async Task Writer_AddsANewSourceToAnExistingProduct()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.EnsureCreatedAsync();
        var writer = new DiscoveryCatalogWriter(dbContext, new CatalogImportService(dbContext));
        var request = CatalogHarvestPlanner.Plan(Response(Candidate()), true).Request!;
        await writer.WriteAsync(request, CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var second = await writer.WriteAsync(
            request with { Provider = "Başka kaynak", SourceUrl = "https://www.displayspecifications.com/en/model/q27g3xmn" },
            CancellationToken.None);

        Assert.NotNull(second.Product);
        Assert.Equal(2, await dbContext.ProductSources.CountAsync(source => source.ProductId == second.Product.Id));
    }

    [Fact]
    public async Task Writer_DeduplicatesProductsWithoutGtinOrMpn()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.EnsureCreatedAsync();
        var writer = new DiscoveryCatalogWriter(dbContext, new CatalogImportService(dbContext));
        var request = CatalogHarvestPlanner.Plan(Response(Candidate(mpn: null)), true).Request!;

        await writer.WriteAsync(request, CancellationToken.None);
        var repeat = await writer.WriteAsync(request, CancellationToken.None);

        Assert.NotNull(repeat.Product);
        Assert.Equal(1, await dbContext.EquipmentProducts.CountAsync(product => product.DisplayName == "AOC Q27G3XMN"));
    }

    private static FitMatrixDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FitMatrixDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new FitMatrixDbContext(options);
    }
}
