using FitMatrix.Api.Data;
using FitMatrix.Api.DTOs;
using FitMatrix.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FitMatrix.Api.Tests;

public sealed class CatalogSnapshotTests
{
    [Fact]
    public async Task Snapshot_RoundTripsHarvestedProductsIntoAFreshDatabase()
    {
        var document = await ExportWithOneHarvestedMonitorAsync();

        await using var fresh = await CreateSeededContextAsync();
        var before = await fresh.EquipmentProducts.CountAsync();
        var inserted = await CatalogSnapshot.ApplyAsync(fresh, document);

        var monitor = await fresh.EquipmentProducts.Include(product => product.Sources).SingleAsync(product => product.ModelName == "Q27G3XMN-TEST");
        Assert.True(inserted >= 1);
        Assert.Equal(before + inserted, await fresh.EquipmentProducts.CountAsync());
        Assert.Equal(61.4m, monitor.WidthCm);
        Assert.Single(monitor.Sources);
        Assert.True(monitor.IsVerified);
    }

    [Fact]
    public async Task Snapshot_IsAppliedOnceAndDoesNotResurrectDeletedProducts()
    {
        var document = await ExportWithOneHarvestedMonitorAsync();
        await using var fresh = await CreateSeededContextAsync();
        await CatalogSnapshot.ApplyAsync(fresh, document);

        fresh.EquipmentProducts.Remove(await fresh.EquipmentProducts.SingleAsync(product => product.ModelName == "Q27G3XMN-TEST"));
        await fresh.SaveChangesAsync();
        var secondRun = await CatalogSnapshot.ApplyAsync(fresh, document);

        Assert.Equal(0, secondRun);
        Assert.False(await fresh.EquipmentProducts.AnyAsync(product => product.ModelName == "Q27G3XMN-TEST"));
    }

    [Fact]
    public async Task Snapshot_KeepsLocalProductsWithTheSameIdentity()
    {
        var document = await ExportWithOneHarvestedMonitorAsync();
        await using var fresh = await CreateSeededContextAsync();
        var writer = new DiscoveryCatalogWriter(fresh, new CatalogImportService(fresh));
        await writer.WriteAsync(MonitorRequest() with { WidthCm = 70m, FootprintWidthCm = 70m }, CancellationToken.None);

        await CatalogSnapshot.ApplyAsync(fresh, document);

        var monitor = await fresh.EquipmentProducts.SingleAsync(product => product.ModelName == "Q27G3XMN-TEST");
        Assert.Equal(70m, monitor.WidthCm);
    }

    [Fact]
    public async Task Snapshot_VersionDependsOnlyOnContent()
    {
        var first = await ExportWithOneHarvestedMonitorAsync();
        var second = await ExportWithOneHarvestedMonitorAsync();

        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.ProductCount, first.Products.Count);
    }

    private static async Task<CatalogSnapshotDocument> ExportWithOneHarvestedMonitorAsync()
    {
        await using var source = await CreateSeededContextAsync();
        var writer = new DiscoveryCatalogWriter(source, new CatalogImportService(source));
        await writer.WriteAsync(MonitorRequest(), CancellationToken.None);
        return await CatalogSnapshot.ExportAsync(source);
    }

    private static DiscoveryCatalogRequestDto MonitorRequest() => new(
        "Resmî üretici web kataloğu",
        "AOC Q27G3XMN Test",
        "AOC",
        "Q27G3XMN-TEST",
        "Q27G3XMN-TEST",
        null,
        61.4m,
        52m,
        30.2m,
        61.4m,
        30.2m,
        6.1m,
        "display",
        "monitor",
        "#252a2e",
        "https://www.aoc.com/monitors/q27g3xmn-test",
        "official-structured",
        null);

    private static async Task<FitMatrixDbContext> CreateSeededContextAsync()
    {
        var options = new DbContextOptionsBuilder<FitMatrixDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new FitMatrixDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }
}
