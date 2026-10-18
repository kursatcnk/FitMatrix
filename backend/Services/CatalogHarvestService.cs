using System.Net;
using System.Text.RegularExpressions;
using FitMatrix.Api.Data;
using FitMatrix.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace FitMatrix.Api.Services;

// Grows the local catalog in the background. Two job kinds share one runner:
//  - model lists: curated model names sent through the editor's discovery pipeline;
//  - manufacturers: product pages taken straight from manufacturers' published sitemaps.
// Only results with three published body dimensions and a source URL are stored, so the
// catalog never contains guessed sizes.
public sealed partial class CatalogHarvestService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<CatalogHarvestService> logger)
{
    private const int RecentLimit = 60;
    private const int MaxConsecutiveFailures = 12;
    private static readonly string ListDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Harvest");
    private static readonly string ManufacturerFile = Path.Combine(ListDirectory, "manufacturers.json");
    private static readonly ProductProviderStatusDto SitemapProvider = new("Üretici ürün haritası", true, string.Empty);

    private readonly Lock gate = new();
    private readonly LinkedList<CatalogHarvestEntryDto> recent = new();
    private CancellationTokenSource? cancellation;
    private DateTimeOffset? startedAt;
    private DateTimeOffset? finishedAt;
    private int total;
    private int processed;
    private int imported;
    private int updated;
    private int alreadyInCatalog;
    private int noVerifiedMatch;
    private int failed;
    private string? currentQuery;

    public IReadOnlyList<CatalogHarvestListDto> GetLists() =>
        Directory.Exists(ListDirectory)
            ? Directory.EnumerateFiles(ListDirectory, "*.txt")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new CatalogHarvestListDto(
                    Path.GetFileNameWithoutExtension(path),
                    CatalogHarvestPlanner.ParseQueryList(File.ReadLines(path)).Count))
                .ToList()
            : [];

    public IReadOnlyList<ManufacturerSource> GetManufacturers() => ManufacturerSitemapReader.LoadSources(ManufacturerFile);

    public IReadOnlyList<CatalogHarvestQuery> ResolveQueries(CatalogHarvestStartRequest request)
    {
        var queries = new List<CatalogHarvestQuery>(CatalogHarvestPlanner.ParseQueryList(request.Queries ?? []));
        var available = GetLists().Select(list => list.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requestedLists = request.Lists is { Count: > 0 } ? request.Lists : request.Queries is { Count: > 0 } ? [] : available.ToList();
        foreach (var name in requestedLists.Where(available.Contains))
            queries.AddRange(CatalogHarvestPlanner.ParseQueryList(File.ReadLines(Path.Combine(ListDirectory, $"{name}.txt"))));

        var unique = queries.DistinctBy(query => query.Query, StringComparer.OrdinalIgnoreCase).ToList();
        return request.MaxQueries is > 0 ? unique.Take(request.MaxQueries.Value).ToList() : unique;
    }

    public bool TryStart(IReadOnlyList<CatalogHarvestQuery> queries, bool requireVerified, TimeSpan delay) =>
        TryBegin(queries.Count, token => RunQueriesAsync(queries, requireVerified, delay, token));

    public bool TryStartManufacturers(IReadOnlyList<ManufacturerSource> sources, TimeSpan delay, int? maxPagesPerSource) =>
        TryBegin(0, token => RunManufacturersAsync(sources, delay, maxPagesPerSource, token));

    public bool Cancel()
    {
        lock (gate)
        {
            if (cancellation is null) return false;
            cancellation.Cancel();
            return true;
        }
    }

    public CatalogHarvestStatusDto GetStatus()
    {
        lock (gate)
        {
            return new CatalogHarvestStatusDto(
                cancellation is not null,
                startedAt,
                finishedAt,
                total,
                processed,
                imported,
                updated,
                alreadyInCatalog,
                noVerifiedMatch,
                failed,
                currentQuery,
                recent.ToList());
        }
    }

    private bool TryBegin(int initialTotal, Func<CancellationToken, Task> body)
    {
        lock (gate)
        {
            if (cancellation is not null) return false;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            startedAt = DateTimeOffset.UtcNow;
            finishedAt = null;
            total = initialTotal;
            processed = imported = updated = alreadyInCatalog = noVerifiedMatch = failed = 0;
            currentQuery = null;
            recent.Clear();
        }

        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await body(token);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Catalog harvest cancelled.");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Catalog harvest stopped unexpectedly.");
            }
            finally
            {
                lock (gate)
                {
                    cancellation?.Dispose();
                    cancellation = null;
                    currentQuery = null;
                    finishedAt = DateTimeOffset.UtcNow;
                }
            }
        }, CancellationToken.None);
        return true;
    }

    private async Task RunQueriesAsync(IReadOnlyList<CatalogHarvestQuery> queries, bool requireVerified, TimeSpan delay, CancellationToken token)
    {
        var consecutiveFailures = 0;
        foreach (var query in queries)
        {
            token.ThrowIfCancellationRequested();
            lock (gate) currentQuery = query.Query;

            var (outcome, productName, message, usedNetwork) = await ProcessQueryAsync(query, requireVerified, token);
            consecutiveFailures = outcome == CatalogHarvestOutcome.Failed ? consecutiveFailures + 1 : 0;
            Record(query.Query, outcome, productName, message);

            if (consecutiveFailures >= MaxConsecutiveFailures)
            {
                logger.LogWarning("Catalog harvest stopped after {Count} consecutive failures.", consecutiveFailures);
                return;
            }
            // Cached and local-only answers cost the remote sites nothing, so only pause after real requests.
            if (usedNetwork) await Task.Delay(delay, token);
        }
    }

    private async Task RunManufacturersAsync(IReadOnlyList<ManufacturerSource> sources, TimeSpan delay, int? maxPagesPerSource, CancellationToken token)
    {
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            lock (gate) currentQuery = $"{source.Name}: ürün haritası okunuyor";

            IReadOnlyList<Uri> products;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var reader = scope.ServiceProvider.GetRequiredService<ManufacturerSitemapReader>();
                products = await reader.ReadProductUrlsAsync(source, token);
            }
            if (maxPagesPerSource is > 0) products = products.Take(maxPagesPerSource.Value).ToList();
            lock (gate) total += products.Count;

            var consecutiveFailures = 0;
            foreach (var product in products)
            {
                token.ThrowIfCancellationRequested();
                var page = source.PageFor(product);
                lock (gate) currentQuery = page.AbsoluteUri;

                var (outcome, productName, message, usedNetwork) = await ProcessManufacturerPageAsync(source, product, page, token);
                consecutiveFailures = outcome == CatalogHarvestOutcome.Failed ? consecutiveFailures + 1 : 0;
                Record(page.AbsoluteUri, outcome, productName, message);

                if (consecutiveFailures >= MaxConsecutiveFailures)
                {
                    logger.LogWarning("{Source}: skipped after {Count} consecutive failures.", source.Name, consecutiveFailures);
                    break;
                }
                if (usedNetwork) await Task.Delay(delay, token);
            }
        }
    }

    private async Task<(CatalogHarvestOutcome Outcome, string? ProductName, string? Message, bool UsedNetwork)> ProcessQueryAsync(
        CatalogHarvestQuery query,
        bool requireVerified,
        CancellationToken token)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FitMatrixDbContext>();
            var visitKey = PageVisitKey($"query:{query.Query.Trim().ToLowerInvariant()}");
            var now = DateTimeOffset.UtcNow;
            // A model whose dimensions were not published a week ago will not be found again on this rerun.
            var visit = await dbContext.ProductLookupCaches.AsNoTracking()
                .Where(entry => entry.NormalizedQuery == visitKey && entry.ExpiresAt > now)
                .Select(entry => entry.ResponseJson)
                .FirstOrDefaultAsync(token);
            if (visit == "no-dimensions")
                return (CatalogHarvestOutcome.NoVerifiedMatch, null, "Son 30 günde arandı; doğrulanmış ölçü bulunamamıştı.", false);

            var discovery = scope.ServiceProvider.GetRequiredService<ProductDiscoveryService>();
            var startedRequest = DateTimeOffset.UtcNow;
            var response = await discovery.SearchAsync(query.Query, token);
            var usedNetwork = DateTimeOffset.UtcNow - startedRequest > TimeSpan.FromMilliseconds(750);

            var plan = CatalogHarvestPlanner.Plan(response, requireVerified, query.Hint);
            var result = await StoreAsync(scope, plan, usedNetwork, token);
            if (result.Outcome != CatalogHarvestOutcome.Failed && usedNetwork)
                await RememberVisitAsync(dbContext, visitKey, result.Outcome, now, token);
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (!IsDatabaseUnavailable(exception))
        {
            logger.LogWarning(exception, "Catalog harvest failed for {Query}", query.Query);
            return (CatalogHarvestOutcome.Failed, null, exception.Message, true);
        }
    }

    private async Task<(CatalogHarvestOutcome Outcome, string? ProductName, string? Message, bool UsedNetwork)> ProcessManufacturerPageAsync(
        ManufacturerSource source,
        Uri product,
        Uri page,
        CancellationToken token)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FitMatrixDbContext>();
            var pageUrl = page.AbsoluteUri;
            var visitKey = PageVisitKey(pageUrl);
            var now = DateTimeOffset.UtcNow;
            var visit = await dbContext.ProductLookupCaches.AsNoTracking()
                .Where(entry => entry.NormalizedQuery == visitKey && entry.ExpiresAt > now)
                .Select(entry => entry.ResponseJson)
                .FirstOrDefaultAsync(token);
            if (visit is not null)
                return visit == "stored"
                    ? (CatalogHarvestOutcome.AlreadyInCatalog, null, "Son 30 günde kaydedildi.", false)
                    : (CatalogHarvestOutcome.NoVerifiedMatch, null, "Son 30 günde incelendi; kaydedilecek ölçü yoktu.", false);

            var stored = await dbContext.ProductSources.AsNoTracking()
                .Where(item => item.SourceUrl == pageUrl)
                .Select(item => item.Product.DisplayName)
                .FirstOrDefaultAsync(token);
            if (stored is not null) return (CatalogHarvestOutcome.AlreadyInCatalog, stored, null, false);

            var result = await ReadAndStorePageAsync(scope, source, product, page, token);
            if (result.Outcome != CatalogHarvestOutcome.Failed)
                await RememberVisitAsync(dbContext, visitKey, result.Outcome, now, token);
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (!IsDatabaseUnavailable(exception))
        {
            logger.LogWarning(exception, "Catalog harvest failed for {Page}", page);
            return (CatalogHarvestOutcome.Failed, null, exception.Message, true);
        }
    }

    private async Task<(CatalogHarvestOutcome Outcome, string? ProductName, string? Message, bool UsedNetwork)> ReadAndStorePageAsync(
        AsyncServiceScope scope,
        ManufacturerSource source,
        Uri product,
        Uri page,
        CancellationToken token)
    {
        var openWeb = scope.ServiceProvider.GetRequiredService<OpenWebProductLookupService>();
        ExternalProductCandidateDto? candidate;
        try
        {
            candidate = await openWeb.ReadManufacturerPageAsync(page, source.Brand, token);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            logger.LogInformation("{Source} is rate limiting; pausing for a minute.", source.Name);
            await Task.Delay(TimeSpan.FromMinutes(1), token);
            candidate = await openWeb.ReadManufacturerPageAsync(page, source.Brand, token);
        }
        if (candidate is null) return (CatalogHarvestOutcome.NoVerifiedMatch, null, "Sayfada üç eksen gövde ölçüsü yok.", true);
        if (BundleRegex().IsMatch(candidate.ProductName))
            return (CatalogHarvestOutcome.NoVerifiedMatch, candidate.ProductName, "Paket ürün; parçalar ayrı ayrı kataloglanır.", true);

        var hint = source.HintFor(product) ?? ClassifyPage(candidate, product);
        if (hint is null) return (CatalogHarvestOutcome.NoVerifiedMatch, candidate.ProductName, "Masa ekipmanı olarak sınıflandırılamadı.", true);

        var response = new ProductDiscoveryResponseDto(candidate.ProductName, [], candidate, [candidate], SitemapProvider);
        var plan = CatalogHarvestPlanner.Plan(response, requireVerified: true, hint);
        return await StoreAsync(scope, plan, true, token);
    }

    // Manufacturer URLs often redirect, so the stored source URL alone cannot tell a rerun which sitemap
    // pages were already read. Each read page is remembered for 30 days in the lookup cache; the key is
    // hashed because cache keys are limited to 180 characters.
    internal static string PageVisitKey(string pageUrl) =>
        $"{ProductDiscoveryService.CacheVersion}sitemap {Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pageUrl)))}";

    private static async Task RememberVisitAsync(
        FitMatrixDbContext dbContext,
        string visitKey,
        CatalogHarvestOutcome outcome,
        DateTimeOffset now,
        CancellationToken token)
    {
        var marker = outcome is CatalogHarvestOutcome.Imported or CatalogHarvestOutcome.Updated or CatalogHarvestOutcome.AlreadyInCatalog
            ? "stored"
            : "no-dimensions";
        var entry = await dbContext.ProductLookupCaches.SingleOrDefaultAsync(item => item.NormalizedQuery == visitKey, token);
        if (entry is null)
        {
            dbContext.ProductLookupCaches.Add(new Models.ProductLookupCache
            {
                Id = Guid.NewGuid(),
                NormalizedQuery = visitKey,
                ResponseJson = marker,
                CreatedAt = now,
                ExpiresAt = now.AddDays(30)
            });
        }
        else
        {
            entry.ResponseJson = marker;
            entry.CreatedAt = now;
            entry.ExpiresAt = now.AddDays(30);
        }
        await dbContext.SaveChangesAsync(token);
    }

    // A lost database connection would otherwise be booked as a failure for every remaining page;
    // ending the job lets an operator (or a watchdog) restart it, and stored pages are skipped on rerun.
    private static bool IsDatabaseUnavailable(Exception exception) =>
        exception.GetBaseException() is Microsoft.Data.SqlClient.SqlException { Class: >= 20 } or Microsoft.Data.SqlClient.SqlException { Number: -1 or 2 or 53 };

    // Pages without a URL hint (Logitech's /shop/p/<slug>) are classified from their own title and spec text.
    // Generic accessories (cables, cases, receivers) are left out: they do not shape a desk layout.
    internal static ProductClassification? ClassifyPage(ExternalProductCandidateDto candidate, Uri product)
    {
        var text = string.Join(' ',
            candidate.ProductName,
            product.AbsolutePath.Replace('-', ' ').Replace('/', ' '),
            string.Join(' ', candidate.Specifications.Where(spec => spec.Group == "Kimlik").Select(spec => spec.Value)));
        var classification = ProductTaxonomy.Classify(text);
        return classification is { CategorySlug: "accessory", EditorModel: "generic" } ? null : classification;
    }

    private static async Task<(CatalogHarvestOutcome Outcome, string? ProductName, string? Message, bool UsedNetwork)> StoreAsync(
        AsyncServiceScope scope,
        CatalogHarvestPlan plan,
        bool usedNetwork,
        CancellationToken token)
    {
        if (plan.Request is null)
            return (plan.Skip ?? CatalogHarvestOutcome.NoVerifiedMatch, plan.Skip == CatalogHarvestOutcome.AlreadyInCatalog ? plan.Message : null, plan.Message, usedNetwork);

        var writer = scope.ServiceProvider.GetRequiredService<DiscoveryCatalogWriter>();
        var result = await writer.WriteAsync(plan.Request, token);
        return result.Product is null
            ? (CatalogHarvestOutcome.Failed, plan.Request.ProductName, result.Error, usedNetwork)
            : (result.Created ? CatalogHarvestOutcome.Imported : CatalogHarvestOutcome.Updated, result.Product.DisplayName, result.Product.Category.Slug, usedNetwork);
    }

    [GeneratedRegex(@"\b(bundle|kit|set of)\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex BundleRegex();

    private void Record(string query, CatalogHarvestOutcome outcome, string? productName, string? message)
    {
        lock (gate)
        {
            processed++;
            switch (outcome)
            {
                case CatalogHarvestOutcome.Imported: imported++; break;
                case CatalogHarvestOutcome.Updated: updated++; break;
                case CatalogHarvestOutcome.AlreadyInCatalog: alreadyInCatalog++; break;
                case CatalogHarvestOutcome.NoVerifiedMatch: noVerifiedMatch++; break;
                default: failed++; break;
            }
            recent.AddFirst(new CatalogHarvestEntryDto(query, outcome.ToString(), productName, message, DateTimeOffset.UtcNow));
            while (recent.Count > RecentLimit) recent.RemoveLast();
        }
    }
}
