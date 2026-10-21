using FitMatrix.Api.Data;
using FitMatrix.Api.DTOs;
using FitMatrix.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FitMatrix.Api.Services;

public sealed class ProductDiscoveryService(
    FitMatrixDbContext dbContext,
    AccountFreeProductLookupService accountFreeLookup,
    OpenWebProductLookupService openWebLookup,
    ProductGuruLookupService productGuruLookup,
    IcecatProductLookupService icecat,
    ILogger<ProductDiscoveryService> logger)
{
    public const string CacheVersion = "v59|";
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> IdentityNoiseTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "monitor", "display", "screen", "inch", "inches", "mouse", "keyboard", "laptop", "speaker",
        "gaming", "wireless", "wired", "monitör", "ekran", "inç", "inc", "oyuncu", "fare",
        "klavye", "dizüstü", "hoparlör", "kulaklık", "kablolu", "kablosuz"
    };

    public async Task<ProductDiscoveryResponseDto> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var rawQuery = query.Trim();
        var directSource = TryGetPublicProductUri(rawQuery);
        var term = NormalizeInput(rawQuery);
        var searchTerms = ExtractIdentityTerms(term);
        var candidates = await dbContext.EquipmentProducts
            .AsNoTracking()
            .Include(product => product.Brand)
            .Include(product => product.Sources)
            .Include(product => product.Specifications)
            .AsSplitQuery()
            .Where(product => searchTerms.All(searchTerm =>
                product.DisplayName.Contains(searchTerm) ||
                product.ModelName.Contains(searchTerm) ||
                product.Brand.Name.Contains(searchTerm) ||
                (product.ManufacturerPartNumber != null && product.ManufacturerPartNumber.Contains(searchTerm)) ||
                (product.Gtin != null && product.Gtin.Contains(searchTerm))))
            .OrderBy(product => product.DisplayName)
            .Take(30)
            .ToListAsync(cancellationToken);

        var localMatches = candidates
            .Select(product => Map(product, term))
            .OrderByDescending(product => product.MatchScore)
            .ThenBy(product => product.DisplayName)
            .Take(12)
            .ToList();

        ExternalProductCandidateDto? externalMatch = null;
        IReadOnlyList<ExternalProductCandidateDto> externalMatches = [];
        var specificQuery = LooksLikeSpecificProduct(term);
        var freshnessCutoff = DateTimeOffset.UtcNow.AddDays(-90);
        var hasEnrichedExactLocalProduct = candidates.Any(product =>
            IsIdentityMatch(product, term) &&
            product.DataQuality != "template" &&
            product.Specifications.Count > 0 &&
            product.Sources.Any(source => source.LastCheckedAt >= freshnessCutoff));
        if (specificQuery && !hasEnrichedExactLocalProduct)
        {
            externalMatch = await FindCachedOrOnlineAsync(term, directSource, cancellationToken);
            if (externalMatch is not null) externalMatches = [externalMatch];
        }
        else if (!specificQuery)
        {
            externalMatches = await FindBroadCachedOrOnlineAsync(term, cancellationToken);
        }

        var provider = new ProductProviderStatusDto(
            "Hesapsız ürün ağı",
            true,
            specificQuery
                ? "Üyelik gerekmez: web sonuçları, resmî ürün sayfaları, teknik föyler ve PDF veri sayfaları birlikte taranır."
                : $"Genel sorgu çevrimiçi ürün dizinlerinde de tarandı; {externalMatches.Count} gerçek model bulundu. Bir modeli seçerek ölçü taramasını derinleştirebilirsin.");

        return new ProductDiscoveryResponseDto(term, localMatches, externalMatch, externalMatches, provider);
    }

    private async Task<IReadOnlyList<ExternalProductCandidateDto>> FindBroadCachedOrOnlineAsync(
        string term,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeQuery($"broad {term}");
        var now = DateTimeOffset.UtcNow;
        var cached = await dbContext.ProductLookupCaches
            .SingleOrDefaultAsync(entry => entry.NormalizedQuery == normalizedQuery, cancellationToken);
        if (cached is not null && cached.ExpiresAt > now)
            return JsonSerializer.Deserialize<List<ExternalProductCandidateDto>>(cached.ResponseJson, CacheJsonOptions) ?? [];

        var accountFreeTask = SafeBroadLookupAsync(
            () => accountFreeLookup.FindManyAsync(term, 12, cancellationToken),
            "barkod kataloğu",
            term);
        var openWebTask = SafeBroadLookupAsync(
            () => openWebLookup.FindCatalogAsync(term, 48, cancellationToken),
            "açık web ürün dizini",
            term);
        var productGuruTask = SafeBroadLookupAsync(
            () => productGuruLookup.FindManyAsync(term, 60, cancellationToken),
            "ProductGuru ürün indeksi",
            term);
        await Task.WhenAll(accountFreeTask, openWebTask, productGuruTask);

        var matches = accountFreeTask.Result
            .Concat(openWebTask.Result)
            .Concat(productGuruTask.Result)
            .Select(candidate => ApplyClassification(candidate, term))
            .GroupBy(CandidateIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(CandidateQuality).First())
            .OrderByDescending(CandidateQuality)
            .ThenBy(candidate => candidate.ProductName)
            .Take(80)
            .ToList();

        var responseJson = JsonSerializer.Serialize(matches, CacheJsonOptions);
        if (cached is null)
        {
            dbContext.ProductLookupCaches.Add(new ProductLookupCache
            {
                Id = Guid.NewGuid(),
                NormalizedQuery = normalizedQuery,
                ResponseJson = responseJson,
                CreatedAt = now,
                ExpiresAt = BroadCacheExpiry(now, matches.Count)
            });
        }
        else
        {
            cached.ResponseJson = responseJson;
            cached.CreatedAt = now;
            cached.ExpiresAt = BroadCacheExpiry(now, matches.Count);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return matches;
    }

    private static DateTimeOffset BroadCacheExpiry(DateTimeOffset now, int resultCount) =>
        resultCount switch
        {
            0 => now.AddMinutes(5),
            < 50 => now.AddMinutes(15),
            _ => now.AddDays(7)
        };

    private async Task<IReadOnlyList<ExternalProductCandidateDto>> SafeBroadLookupAsync(
        Func<Task<IReadOnlyList<ExternalProductCandidateDto>>> lookup,
        string source,
        string term)
    {
        try
        {
            return await lookup();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or RegexMatchTimeoutException or TaskCanceledException)
        {
            logger.LogInformation(exception, "{Source} genel ürün araması kullanılamadı: {Query}", source, term);
            return [];
        }
    }

    private async Task<ExternalProductCandidateDto?> FindCachedOrOnlineAsync(
        string term,
        Uri? directSource,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeQuery(directSource is null ? term : $"{term} {directSource.Host} {directSource.AbsolutePath}");
        var now = DateTimeOffset.UtcNow;
        var cached = await dbContext.ProductLookupCaches
            .SingleOrDefaultAsync(entry => entry.NormalizedQuery == normalizedQuery, cancellationToken);

        if (cached is not null && cached.ExpiresAt > now)
            return JsonSerializer.Deserialize<ExternalProductCandidateDto>(cached.ResponseJson, CacheJsonOptions);

        ExternalProductCandidateDto? externalMatch = null;
        try
        {
            externalMatch = await accountFreeLookup.FindAsync(term, cancellationToken);
            if (NeedsEnrichment(externalMatch))
            {
                var openWebMatch = await openWebLookup.FindAsync(term, directSource, cancellationToken);
                if (openWebMatch is not null && (externalMatch is null || CandidateQuality(openWebMatch) > CandidateQuality(externalMatch)))
                    externalMatch = openWebMatch;
            }
            if (externalMatch is null && icecat.IsConfigured)
                externalMatch = await icecat.FindAsync(term, cancellationToken);
            if (externalMatch is not null)
                externalMatch = ApplyClassification(externalMatch, term);
        }
        catch (HttpRequestException exception)
        {
            logger.LogInformation(exception, "Account-free product lookup was unavailable for {Query}", term);
        }

        var responseJson = JsonSerializer.Serialize(externalMatch, CacheJsonOptions);
        if (cached is null)
        {
            dbContext.ProductLookupCaches.Add(new ProductLookupCache
            {
                Id = Guid.NewGuid(),
                NormalizedQuery = normalizedQuery,
                ResponseJson = responseJson,
                CreatedAt = now,
                ExpiresAt = externalMatch is null ? now.AddMinutes(5) : now.AddDays(14)
            });
        }
        else
        {
            cached.ResponseJson = responseJson;
            cached.CreatedAt = now;
            cached.ExpiresAt = externalMatch is null ? now.AddMinutes(5) : now.AddDays(14);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return externalMatch;
    }

    private static bool LooksLikeSpecificProduct(string term)
    {
        var parts = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var digits = new string(term.Where(char.IsDigit).ToArray());
        return digits.Length is 8 or 12 or 13 or 14 ||
            (parts.Length >= 2 && term.Any(char.IsDigit)) ||
            (parts.Length >= 3 && term.Length >= 10);
    }

    private static string[] ExtractIdentityTerms(string term)
    {
        var all = Regex.Matches(term, "[\\p{L}\\p{Nd}]+(?:[-_][\\p{L}\\p{Nd}]+)*", RegexOptions.None, TimeSpan.FromMilliseconds(250))
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var meaningful = all.Where(value => !IdentityNoiseTerms.Contains(value)).ToArray();
        if (meaningful.Length > 1)
            meaningful = meaningful.Where(value => !value.All(char.IsDigit) || value.Length > 3).ToArray();
        return meaningful.Length > 0 ? meaningful : all;
    }

    private static bool IsIdentityMatch(EquipmentProduct product, string term)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        var normalized = string.Join(' ', ExtractIdentityTerms(term));
        return product.Gtin?.Equals(normalized, comparison) == true ||
               (normalized.Contains(product.Brand.Name, comparison) &&
                (normalized.Contains(product.ModelName, comparison) ||
                 (product.ManufacturerPartNumber is not null && normalized.Contains(product.ManufacturerPartNumber, comparison)))) ||
               product.ModelName.Equals(normalized, comparison) ||
               product.ManufacturerPartNumber?.Equals(normalized, comparison) == true;
    }

    private static bool NeedsEnrichment(ExternalProductCandidateDto? candidate) =>
        candidate is null ||
        candidate.WidthCm is null || candidate.HeightCm is null || candidate.DepthCm is null ||
        candidate.Specifications.Count < 10;

    private static int CandidateQuality(ExternalProductCandidateDto candidate) =>
        (candidate.MeasurementStatus == "official-structured" ? 240 : 0) +
        (candidate.Provider.Contains("Açık web", StringComparison.OrdinalIgnoreCase) ? 80 : 0) +
        (candidate.WidthCm is not null ? 120 : 0) +
        (candidate.HeightCm is not null ? 120 : 0) +
        (candidate.DepthCm is not null ? 120 : 0) +
        candidate.Specifications.Count;

    private static ExternalProductCandidateDto ApplyClassification(ExternalProductCandidateDto candidate, string term)
    {
        var classificationText = string.Join(' ',
            term,
            candidate.ProductName,
            candidate.ModelName,
            string.Join(' ', candidate.Specifications.Select(item => $"{item.Name} {item.Value}")));
        var classification = ProductTaxonomy.Classify(classificationText);
        return candidate with
        {
            SuggestedCategory = classification.CategorySlug,
            SuggestedEditorModel = classification.EditorModel,
            SuggestedColor = classification.DisplayColor
        };
    }

    private static string CandidateIdentityKey(ExternalProductCandidateDto candidate) =>
        candidate.Gtin ??
        string.Join(' ', Regex.Matches($"{candidate.Brand} {candidate.ProductName}".ToLowerInvariant(), "[\\p{L}\\p{Nd}]+")
            .Select(match => match.Value));

    private static string NormalizeQuery(string term) =>
        CacheVersion + string.Join(' ', Regex.Matches(term.ToLowerInvariant(), "[\\p{L}\\p{Nd}]+(?:[-_][\\p{L}\\p{Nd}]+)*")
            .Select(match => match.Value));

    private static string NormalizeInput(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return input;

        var embeddedUrl = GetQueryValue(uri.Query, "url");
        if (!string.IsNullOrWhiteSpace(embeddedUrl))
        {
            var decoded = DecodeRepeatedly(embeddedUrl);
            if (Uri.TryCreate(decoded, UriKind.Absolute, out var absoluteEmbedded))
                uri = absoluteEmbedded;
            else if (Uri.TryCreate(new Uri("https://product.local"), decoded, out var relativeEmbedded))
                uri = relativeEmbedded;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(DecodeRepeatedly)
            .ToArray();
        var dpIndex = Array.FindIndex(segments, segment => segment.Equals("dp", StringComparison.OrdinalIgnoreCase));
        var candidates = (dpIndex > 0 ? segments.Take(dpIndex) : segments)
            .Where(segment => segment.Length >= 4)
            .Where(segment => !segment.Equals("product", StringComparison.OrdinalIgnoreCase) &&
                              !segment.Equals("products", StringComparison.OrdinalIgnoreCase) &&
                              !segment.Equals("ref", StringComparison.OrdinalIgnoreCase))
            .Select(segment => Regex.Replace(segment, "[^\\p{L}\\p{Nd}]+", " ").Trim())
            .Where(segment => segment.Any(char.IsLetter))
            .OrderByDescending(segment => segment.Length)
            .ToArray();
        return candidates.FirstOrDefault() ?? input;
    }

    private static Uri? TryGetPublicProductUri(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) || (!uri.IsDefaultPort && uri.Port != 443)) return null;
        var host = uri.IdnHost;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out _)) return null;
        return new UriBuilder(uri) { Fragment = string.Empty }.Uri;
    }

    private static string? GetQueryValue(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                return parts[1];
        }
        return null;
    }

    private static string DecodeRepeatedly(string value)
    {
        var decoded = value.Replace('+', ' ');
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var next = WebUtility.UrlDecode(decoded);
            if (next.Equals(decoded, StringComparison.Ordinal)) break;
            decoded = next;
        }
        return decoded;
    }

    private static ProductDiscoveryResultDto Map(EquipmentProduct product, string term) => new(
        product.Id,
        product.Brand.Name,
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
        product.DataQuality,
        product.IsVerified,
        product.Sources.Count,
        Score(product, term));

    private static int Score(EquipmentProduct product, string term)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        var searchTerms = term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (product.Gtin?.Equals(term, comparison) == true) return 100;
        if ($"{product.Brand.Name} {product.ModelName}".Equals(term, comparison)) return 99;
        if (product.ManufacturerPartNumber is not null &&
            $"{product.Brand.Name} {product.ManufacturerPartNumber}".Equals(term, comparison)) return 99;
        if (product.ManufacturerPartNumber?.Equals(term, comparison) == true) return 98;
        if (product.ModelName.Equals(term, comparison)) return 95;
        if (product.DisplayName.Equals(term, comparison)) return 92;
        if (product.DisplayName.StartsWith(term, comparison)) return 82;
        if (product.ModelName.StartsWith(term, comparison)) return 78;
        if (product.Brand.Name.StartsWith(term, comparison)) return 72;
        if (searchTerms.Length > 1) return 88;
        return 60;
    }
}
