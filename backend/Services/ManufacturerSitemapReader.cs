using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FitMatrix.Api.Services;

public sealed record ManufacturerUrlHint(string Pattern, string Category, string Model);

// One manufacturer catalogue: where its sitemaps live and which URLs are desk-relevant product pages.
public sealed record ManufacturerSource(
    string Name,
    string Brand,
    IReadOnlyList<string> Sitemaps,
    string Include,
    string? ChildInclude = null,
    string? Exclude = null,
    string? PageSuffix = null,
    IReadOnlyList<ManufacturerUrlHint>? Hints = null,
    int? DedupePathSegments = null)
{
    // Colour/SKU variants share the leading path segments (/gaming-mice/razer-viper-v3-pro/RZ01-...).
    public string DedupeKey(Uri uri)
    {
        if (DedupePathSegments is not > 0) return uri.AbsoluteUri;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Take(DedupePathSegments.Value);
        return $"{uri.Host}/{string.Join('/', segments)}";
    }

    public Uri PageFor(Uri productUri) =>
        string.IsNullOrEmpty(PageSuffix) ? productUri : new Uri(productUri, PageSuffix);

    public ProductClassification? HintFor(Uri pageUri)
    {
        foreach (var hint in Hints ?? [])
            if (Regex.IsMatch(pageUri.AbsoluteUri, hint.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
                return ProductTaxonomy.FromHint(hint.Category, hint.Model);
        return null;
    }
}

// Collects product page URLs from manufacturers' published sitemaps and honours their robots.txt.
public sealed partial class ManufacturerSitemapReader(HttpClient httpClient, ILogger<ManufacturerSitemapReader> logger)
{
    private const int MaxSitemapsPerSource = 400;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, IReadOnlyList<string>> robotsCache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ManufacturerSource> LoadSources(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<List<ManufacturerSource>>(File.ReadAllText(path), JsonOptions) ?? []
            : [];

    public async Task<IReadOnlyList<Uri>> ReadProductUrlsAsync(ManufacturerSource source, CancellationToken cancellationToken)
    {
        var include = new Regex(source.Include, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        var exclude = source.Exclude is null ? null : new Regex(source.Exclude, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        var childInclude = source.ChildInclude is null ? null : new Regex(source.ChildInclude, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));

        var pending = new Queue<string>(source.Sitemaps);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var products = new List<Uri>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0 && visited.Count < MaxSitemapsPerSource)
        {
            var sitemap = pending.Dequeue();
            if (!visited.Add(sitemap)) continue;
            var xml = await DownloadAsync(sitemap, cancellationToken);
            if (xml is null) continue;

            var isIndex = xml.Contains("<sitemapindex", StringComparison.OrdinalIgnoreCase);
            foreach (Match location in LocationRegex().Matches(xml))
            {
                var value = WebUtility.HtmlDecode(location.Groups["url"].Value.Trim());
                if (isIndex)
                {
                    if (childInclude is null || childInclude.IsMatch(value)) pending.Enqueue(value);
                    continue;
                }
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) continue;
                var clean = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri;
                if (!include.IsMatch(clean.AbsoluteUri) || exclude?.IsMatch(clean.AbsoluteUri) == true) continue;
                if (seen.Add(source.DedupeKey(clean))) products.Add(clean);
            }
        }

        var allowed = new List<Uri>(products.Count);
        foreach (var product in products)
            if (await IsAllowedAsync(product, cancellationToken)) allowed.Add(product);
        logger.LogInformation("{Source}: {Count} product pages from {Sitemaps} sitemaps", source.Name, allowed.Count, visited.Count);
        return allowed;
    }

    public async Task<bool> IsAllowedAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!robotsCache.TryGetValue(uri.Host, out var rules))
        {
            var robots = await DownloadAsync($"{uri.Scheme}://{uri.Host}/robots.txt", cancellationToken);
            rules = ParseDisallowRules(robots ?? string.Empty);
            robotsCache[uri.Host] = rules;
        }
        var path = uri.PathAndQuery;
        return !rules.Any(rule => RuleMatches(rule, path));
    }

    // Disallow lines of the "User-agent: *" group; FitMatrix announces no named agent of its own.
    internal static IReadOnlyList<string> ParseDisallowRules(string robots)
    {
        var rules = new List<string>();
        var inWildcardGroup = false;
        var groupHasRules = false;
        foreach (var rawLine in robots.Split('\n'))
        {
            var line = rawLine.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var field = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (field.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (groupHasRules) { inWildcardGroup = false; groupHasRules = false; }
                inWildcardGroup |= value == "*";
            }
            else if (field.Equals("disallow", StringComparison.OrdinalIgnoreCase) || field.Equals("allow", StringComparison.OrdinalIgnoreCase))
            {
                groupHasRules = true;
                if (inWildcardGroup && field.Equals("disallow", StringComparison.OrdinalIgnoreCase) && value.Length > 0) rules.Add(value);
            }
        }
        return rules;
    }

    internal static bool RuleMatches(string rule, string path)
    {
        var anchored = rule.EndsWith('$');
        var pattern = "^" + Regex.Escape(anchored ? rule[..^1] : rule).Replace("\\*", ".*") + (anchored ? "$" : string.Empty);
        return Regex.IsMatch(path, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
    }

    private async Task<string?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength > 60_000_000) return null;
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(exception, "Sitemap could not be read: {Url}", url);
            return null;
        }
    }

    [GeneratedRegex("<loc>\\s*(?<url>[^<]+?)\\s*</loc>", RegexOptions.IgnoreCase, 5000)]
    private static partial Regex LocationRegex();
}
