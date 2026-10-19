using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FitMatrix.Api.DTOs;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FitMatrix.Api.Services;

public sealed partial class OpenWebProductLookupService(
    HttpClient httpClient,
    ILogger<OpenWebProductLookupService> logger)
{
    private static readonly string[] TrustedSpecificationHosts =
    [
        "displayspecifications.com", "rtings.com", "pangoly.com", "techpowerup.com",
        "productchart.com", "manua.ls", "mousespecs.org", "mousemetric.com", "kbdlab.io",
        "betterbuybase.com"
    ];
    private static readonly string[] DisallowedSourceHosts =
    [
        "glarity.app", "perplexity.ai", "chatgpt.com", "poe.com", "you.com"
    ];
    private static readonly HashSet<string> SearchNoiseTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "monitor", "monitör", "display", "screen", "ekran", "gaming", "oyuncu", "mouse", "fare",
        "keyboard", "klavye", "laptop", "notebook", "speaker", "hoparlör", "headset", "kulaklık",
        "headphones", "earbuds", "router", "modem", "switch", "access", "point", "network", "wifi", "wi-fi",
        "desk", "table", "masa", "chair", "sandalye", "camera", "kamera", "webcam", "printer", "yazıcı",
        "console", "konsol", "phone", "telefon", "tablet", "lamp", "lamba", "stand", "arm", "mount",
        "inch", "inches", "inç", "inc", "specification", "specifications", "datasheet", "dimensions",
        "dimension", "ölçü", "ölçüleri", "fhd", "qhd", "wqhd", "uhd", "oled", "ips", "va"
    };
    private static readonly IReadOnlyDictionary<string, string[]> OfficialBrandDomains =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["aoc"] = ["aoc.com"],
            ["acer"] = ["acer.com"],
            ["akko"] = ["en.akkogear.com", "akkogear.com"],
            ["alienware"] = ["dell.com"],
            ["amazonbasics"] = ["amazon.com"],
            ["anker"] = ["anker.com"],
            ["apc"] = ["apc.com"],
            ["apple"] = ["apple.com"],
            ["arctic"] = ["arctic.de"],
            ["asus"] = ["asus.com"],
            ["asrock"] = ["asrock.com"],
            ["bequiet"] = ["bequiet.com"],
            ["benq"] = ["benq.com"],
            ["brother"] = ["brother.com"],
            ["canon"] = ["canon.com"],
            ["cherry"] = ["cherry.de", "cherry-world.com"],
            ["coolermaster"] = ["coolermaster.com"],
            ["corsair"] = ["corsair.com"],
            ["creativelabs"] = ["creative.com"],
            ["dlink"] = ["dlink.com"],
            ["dell"] = ["dell.com"],
            ["ducky"] = ["ducky.global"],
            ["elgato"] = ["elgato.com"],
            ["epson"] = ["epson.com"],
            ["ergotron"] = ["ergotron.com"],
            ["flexispot"] = ["flexispot.com"],
            ["fractaldesign"] = ["fractal-design.com"],
            ["fujifilm"] = ["fujifilm.com"],
            ["glorious"] = ["gloriousgaming.com"],
            ["gigabyte"] = ["gigabyte.com"],
            ["google"] = ["store.google.com", "google.com"],
            ["hp"] = ["hp.com"],
            ["hpe"] = ["hpe.com"],
            ["hermanmiller"] = ["hermanmiller.com"],
            ["hyperx"] = ["hyperx.com"],
            ["ikea"] = ["ikea.com"],
            ["jbl"] = ["jbl.com"],
            ["keychron"] = ["keychron.com"],
            ["kobo"] = ["kobo.com"],
            ["konicaminolta"] = ["konicaminolta.com"],
            ["lamzu"] = ["lamzu.com"],
            ["leopold"] = ["leopold.co.kr"],
            ["lenovo"] = ["lenovo.com"],
            ["lg"] = ["lg.com"],
            ["lianli"] = ["lian-li.com"],
            ["logitech"] = ["logitech.com", "logitechg.com"],
            ["logitechg"] = ["logitechg.com", "logitech.com"],
            ["madcatz"] = ["madcatz.com"],
            ["marshall"] = ["marshall.com"],
            ["montech"] = ["montechpc.com"],
            ["microsoft"] = ["microsoft.com"],
            ["msi"] = ["msi.com"],
            ["netgear"] = ["netgear.com"],
            ["nintendo"] = ["nintendo.com"],
            ["noctua"] = ["noctua.at"],
            ["nuphy"] = ["nuphy.com"],
            ["nzxt"] = ["nzxt.com"],
            ["oneplus"] = ["oneplus.com"],
            ["phanteks"] = ["phanteks.com"],
            ["oppo"] = ["oppo.com"],
            ["panasonic"] = ["panasonic.com"],
            ["philips"] = ["philips.com"],
            ["pulsar"] = ["pulsar.gg"],
            ["razer"] = ["razer.com"],
            ["realme"] = ["realme.com"],
            ["roccat"] = ["roccat.com", "turtlebeach.com"],
            ["samsung"] = ["samsung.com"],
            ["secretlab"] = ["secretlab.co"],
            ["sennheiser"] = ["sennheiser-hearing.com", "sennheiser.com"],
            ["shure"] = ["shure.com"],
            ["sony"] = ["sony.com"],
            ["steelseries"] = ["steelseries.com"],
            ["synology"] = ["synology.com"],
            ["thermaltake"] = ["thermaltake.com"],
            ["tplink"] = ["tp-link.com"],
            ["turtlebeach"] = ["turtlebeach.com"],
            ["ubiquiti"] = ["ui.com"],
            ["viewsonic"] = ["viewsonic.com"],
            ["vivo"] = ["vivo.com"],
            ["varmilo"] = ["varmilo.com"],
            ["wooting"] = ["wooting.io"],
            ["xiaomi"] = ["mi.com"],
            ["xerox"] = ["xerox.com"],
            ["zowie"] = ["zowie.benq.com", "benq.com"]
        };

    public async Task<ExternalProductCandidateDto?> FindAsync(
        string query,
        Uri? directSource,
        CancellationToken cancellationToken)
    {
        var identity = ParseIdentity(query);
        if (identity.Model is null) return null;

        ExternalProductCandidateDto? best = null;
        var directUrls = BuildDirectManufacturerUrls(identity, query).ToList();
        if (directSource is not null && !IsUnsafeSourceUri(directSource) && !IsDisallowedSourceHost(directSource.Host))
            directUrls.Insert(0, directSource);
        directUrls = directUrls.DistinctBy(url => url.AbsoluteUri, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var directUrl in directUrls)
        {
            var directResult = await ReadProductDocumentAsync(directUrl, identity, cancellationToken);
            if (directResult is null) continue;
            if (best is null || QualityScore(directResult) > QualityScore(best)) best = directResult;
            if (IsComplete(directResult))
                return directResult;
        }

        var visitedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directUrl in directUrls)
            visitedSources.Add(directUrl.AbsoluteUri);

        foreach (var searchUrl in BuildSearchUrls(identity))
        {
            try
            {
                using var searchResponse = await httpClient.GetAsync(searchUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!searchResponse.IsSuccessStatusCode) continue;
                var searchLimit = searchUrl.AbsolutePath.Contains("sitemap", StringComparison.OrdinalIgnoreCase)
                    ? 50_000_000
                    : 2_000_000;
                var searchHtml = await ReadLimitedStringAsync(searchResponse.Content, searchLimit, cancellationToken);
                if (searchHtml is null) continue;

                var candidates = ExtractResultUrls(searchHtml, identity)
                    .Where(url => visitedSources.Add(url.AbsoluteUri))
                    .Select(url => new { Url = url, Score = ScoreSource(url, identity) })
                    .Where(candidate => candidate.Score > 0)
                    .OrderByDescending(candidate => candidate.Score)
                    .Take(20)
                    .ToList();

                foreach (var candidate in candidates)
                {
                    var result = await ReadProductDocumentAsync(candidate.Url, identity, cancellationToken);
                    if (result is null) continue;
                    if (best is null || QualityScore(result) > QualityScore(best)) best = result;
                    if (IsComplete(result))
                        return result;
                }

                var snippetResult = ReadSearchResultSnippets(searchHtml, searchUrl, identity);
                if (snippetResult is not null && (best is null || QualityScore(snippetResult) > QualityScore(best)))
                    best = snippetResult;
            }
            catch (Exception exception) when (exception is HttpRequestException or RegexMatchTimeoutException or TaskCanceledException)
            {
                logger.LogInformation(exception, "Open web search source was unavailable for {Query}", query);
            }
        }

        return best;
    }

    public async Task<IReadOnlyList<ExternalProductCandidateDto>> FindCatalogAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var catalogQuery = TranslateCatalogQuery(query);
        var discovered = new List<CatalogSearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var catalogUri in BuildCatalogIndexUrls(catalogQuery))
        {
            try
            {
                using var response = await httpClient.GetAsync(catalogUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;
                var html = await ReadLimitedStringAsync(response.Content, 3_000_000, cancellationToken);
                if (html is null) continue;

                foreach (Match match in CatalogAnchorRegex().Matches(WebUtility.HtmlDecode(html)))
                {
                    var title = CleanHtml(match.Groups["title"].Value);
                    var rawUrl = match.Groups["url"].Value;
                    if (!Uri.TryCreate(catalogUri, rawUrl, out var resolved) || IsUnsafeSourceUri(resolved) ||
                        IsDisallowedSourceHost(resolved.Host) || !LooksLikeCatalogProduct(title, resolved) ||
                        !CatalogResultMatchesQuery(title, resolved, catalogQuery)) continue;
                    var clean = new UriBuilder(resolved) { Query = string.Empty, Fragment = string.Empty }.Uri;
                    if (seen.Add(clean.AbsoluteUri)) discovered.Add(new CatalogSearchResult(title, clean));
                    if (discovered.Count >= Math.Clamp(limit, 1, 64) * 3) break;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or RegexMatchTimeoutException or TaskCanceledException)
            {
                logger.LogInformation(exception, "Manufacturer catalog was unavailable for {Query}", query);
            }
        }

        foreach (var phrase in BuildCatalogSearchPhrases(catalogQuery))
        {
            try
            {
                var searchUri = new Uri($"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(phrase)}");
                using var response = await httpClient.GetAsync(searchUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;
                var html = await ReadLimitedStringAsync(response.Content, 2_000_000, cancellationToken);
                if (html is null) continue;

                foreach (Match match in GenericSearchResultRegex().Matches(WebUtility.HtmlDecode(html)))
                {
                    var title = CleanHtml(match.Groups["title"].Value);
                    var rawUrl = match.Groups["url"].Value;
                    if (rawUrl.StartsWith("//", StringComparison.Ordinal)) rawUrl = $"https:{rawUrl}";
                    var resolved = ResolveSearchResultUri(rawUrl);
                    if (resolved is null || IsSearchHost(resolved.Host) || IsUnsafeSourceUri(resolved) ||
                        IsDisallowedSourceHost(resolved.Host) || !LooksLikeCatalogProduct(title, resolved) ||
                        !CatalogResultMatchesQuery(title, resolved, catalogQuery)) continue;
                    var clean = new UriBuilder(resolved) { Query = string.Empty, Fragment = string.Empty }.Uri;
                    if (seen.Add(clean.AbsoluteUri)) discovered.Add(new CatalogSearchResult(title, clean));
                    if (discovered.Count >= Math.Clamp(limit, 1, 64) * 3) break;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or RegexMatchTimeoutException or TaskCanceledException)
            {
                logger.LogInformation(exception, "Open catalog search source was unavailable for {Query}", query);
            }
        }

        var deduplicated = discovered
            .GroupBy(result => NormalizeCompact(result.Title), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var requestedLimit = Math.Clamp(limit, 1, 64);
        var sourceCount = Math.Max(1, deduplicated.Select(result => result.Url.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var perSource = Math.Max(4, (int)Math.Ceiling((double)requestedLimit / sourceCount));
        var balanced = deduplicated
            .GroupBy(result => result.Url.Host, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.Take(perSource))
            .ToList();
        var selected = balanced
            .Concat(deduplicated.Where(result => !balanced.Contains(result)))
            .Take(requestedLimit)
            .ToArray();
        var tasks = selected.Select(result => EnrichCatalogResultAsync(result, catalogQuery, cancellationToken));
        var enriched = await Task.WhenAll(tasks);
        return enriched.Where(result => result is not null).Cast<ExternalProductCandidateDto>().ToList();
    }

    private async Task<ExternalProductCandidateDto?> EnrichCatalogResultAsync(
        CatalogSearchResult result,
        string catalogQuery,
        CancellationToken cancellationToken)
    {
        var identity = ParseIdentity($"{result.Title} {catalogQuery}");
        if (identity.Model is null) return null;
        var officialHostBrand = DetectBrandFromHost(result.Url.Host);

        var enriched = await ReadProductDocumentAsync(result.Url, identity, cancellationToken);
        if (enriched is not null)
            return enriched with { ProductName = result.Title, Brand = officialHostBrand ?? enriched.Brand };

        return new ExternalProductCandidateDto(
            "Açık web ürün dizini",
            result.Title,
            officialHostBrand ?? identity.Brand,
            identity.Model,
            identity.Model,
            null,
            null,
            null,
            null,
            null,
            result.Url.ToString(),
            "identity-only",
            "Gerçek ürün sayfası bulundu; fiziksel ölçüleri derin model taramasıyla tamamlamak için bu sonucu seç.",
            [
                new ExternalSpecificationDto("Kimlik", "Marka", identity.Brand ?? "—", null),
                new ExternalSpecificationDto("Kimlik", "Model", identity.Model, null),
                new ExternalSpecificationDto("Kaynak", "Ürün sayfası", result.Url.Host, null)
            ]);
    }

    private static IReadOnlyList<string> BuildCatalogSearchPhrases(string query)
    {
        var normalized = NormalizeCompact(query);
        List<string> phrases;
        if (normalized.Contains("keyboard", StringComparison.Ordinal))
            phrases =
            [
                $"{query} product dimensions specifications",
                "site:keychron.com/products keyboard dimensions",
                "site:logitech.com keyboard technical specifications dimensions",
                "site:razer.com keyboard specifications dimensions",
                "site:corsair.com keyboard dimensions",
                "site:steelseries.com gaming keyboard dimensions",
                "site:hyperx.com keyboard dimensions",
                "site:wooting.io keyboard dimensions",
                "site:nuphy.com keyboard dimensions"
            ];
        else if (normalized.Contains("mouse", StringComparison.Ordinal))
            phrases =
            [
                $"{query} product dimensions specifications",
                "site:logitech.com mouse technical specifications dimensions",
                "site:razer.com mouse specifications dimensions",
                "site:steelseries.com mouse dimensions",
                "site:corsair.com mouse dimensions",
                "site:hyperx.com mouse dimensions",
                "site:pulsar.gg gaming mouse dimensions",
                "site:gloriousgaming.com mouse dimensions"
            ];
        else if (normalized.Contains("monitor", StringComparison.Ordinal) || normalized.Contains("display", StringComparison.Ordinal))
            phrases =
            [
                $"{query} product dimensions specifications",
                "site:dell.com monitor dimensions specifications",
                "site:msi.com monitor specifications dimensions",
                "site:aoc.com monitor dimensions",
                "site:lg.com monitor dimensions specifications",
                "site:samsung.com monitor dimensions specifications",
                "site:asus.com monitor dimensions specifications",
                "site:benq.com monitor dimensions specifications",
                "site:viewsonic.com monitor dimensions specifications"
            ];
        else if (normalized.Contains("desk", StringComparison.Ordinal) || normalized.Contains("table", StringComparison.Ordinal))
            phrases =
            [
                "site:ikea.com desk product dimensions",
                "site:flexispot.com desk dimensions specifications",
                "site:secretlab.co desk dimensions specifications",
                $"{query} product dimensions"
            ];
        else if (normalized.Contains("chair", StringComparison.Ordinal))
            phrases =
            [
                "office chair product dimensions specifications",
                "gaming chair product dimensions specifications",
                "site:hermanmiller.com chair dimensions",
                "site:secretlab.co chair dimensions",
                $"{query} product dimensions"
            ];
        else if (normalized.Contains("headset", StringComparison.Ordinal) || normalized.Contains("headphone", StringComparison.Ordinal))
            phrases =
            [
                $"{query} product dimensions specifications",
                "site:logitechg.com headset dimensions specifications",
                "site:razer.com headset dimensions specifications",
                "site:steelseries.com headset dimensions specifications",
                "site:hyperx.com headset dimensions specifications",
                "site:sennheiser-hearing.com headphones dimensions"
            ];
        else if (normalized.Contains("case", StringComparison.Ordinal) || normalized.Contains("tower", StringComparison.Ordinal))
            phrases =
            [
                $"{query} product dimensions specifications",
                "site:asus.com pc case dimensions specifications",
                "site:coolermaster.com pc case dimensions specifications",
                "site:corsair.com pc case dimensions specifications",
                "site:nzxt.com pc case dimensions",
                "site:fractal-design.com case dimensions",
                "site:lian-li.com case dimensions",
                "site:bequiet.com case dimensions"
            ];
        else
            phrases = [$"{query} product model dimensions specifications", $"{query} technical specifications dimensions"];

        var identity = ParseIdentity(query);
        if (identity.Brand is not null && OfficialBrandDomains.TryGetValue(NormalizeCompact(identity.Brand), out var domains))
            phrases.InsertRange(0, domains.Select(domain => $"site:{domain} \"{query}\" dimensions specifications"));
        return phrases.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
    }

    private static IReadOnlyList<Uri> BuildCatalogIndexUrls(string query)
    {
        var normalized = NormalizeCompact(query);
        if (normalized.Contains("keyboard", StringComparison.Ordinal))
            return
            [
                new Uri("https://www.razer.com/pc/gaming-keyboards-and-keypads"),
                new Uri("https://www.logitechg.com/en-us/products/gaming-keyboards.html"),
                new Uri("https://www.keychron.com/collections/all-keyboards"),
                new Uri("https://www.corsair.com/us/en/c/keyboards"),
                new Uri("https://steelseries.com/gaming-keyboards"),
                new Uri("https://hyperx.com/collections/gaming-keyboards"),
                new Uri("https://wooting.io/our-products"),
                new Uri("https://nuphy.com/collections/keyboards"),
                new Uri("https://en.akkogear.com/store/keyboard/")
            ];
        if (normalized.Contains("mouse", StringComparison.Ordinal))
            return
            [
                new Uri("https://www.razer.com/pc/gaming-mice"),
                new Uri("https://www.logitechg.com/en-us/products/gaming-mice.html"),
                new Uri("https://www.keychron.com/collections/mice"),
                new Uri("https://www.corsair.com/us/en/c/gaming-mouse"),
                new Uri("https://steelseries.com/gaming-mice"),
                new Uri("https://hyperx.com/collections/gaming-mice"),
                new Uri("https://www.pulsar.gg/collections/mice"),
                new Uri("https://www.gloriousgaming.com/collections/gaming-mice")
            ];
        if (normalized.Contains("headset", StringComparison.Ordinal) || normalized.Contains("headphone", StringComparison.Ordinal))
            return
            [
                new Uri("https://www.razer.com/pc/gaming-headsets-and-audio"),
                new Uri("https://www.logitechg.com/en-us/products/gaming-audio.html")
            ];
        if (normalized.Contains("chair", StringComparison.Ordinal))
            return [new Uri("https://www.razer.com/gaming-chairs")];
        return [];
    }

    private static string TranslateCatalogQuery(string query)
    {
        var translated = query;
        foreach (var (source, target) in new (string Source, string Target)[]
        {
            ("bilgisayar kasası", "pc case"), ("monitör kolu", "monitor arm"),
            ("klavye", "keyboard"), ("fare", "mouse"), ("monitör", "monitor"), ("ekran", "monitor"),
            ("kasa", "pc case"), ("masa", "desk"), ("sandalye", "chair"),
            ("kulaklık", "headset"), ("hoparlör", "speaker"), ("yazıcı", "printer"), ("kamera", "webcam"),
            ("telefon", "smartphone"), ("lamba", "desk lamp")
        })
            translated = Regex.Replace(translated, $"(?<![\\p{{L}}\\p{{Nd}}]){Regex.Escape(source)}(?![\\p{{L}}\\p{{Nd}}])", target, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        return translated;
    }

    private static bool LooksLikeCatalogProduct(string title, Uri uri)
    {
        if (title.Length is < 4 or > 240) return false;
        if (Regex.IsMatch(title, "\\b(guide|comparison|explained|best|manual|support|driver|review|sizes?|learn more)\\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
            return false;
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var productPath = path.Contains("/product", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("/p/", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("/shop/", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("/store/", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("/collections/", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("/gaming-gear/", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("/peripherals/", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("keyboard", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("mouse", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("monitor", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("display", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("desk", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("chair", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("headset", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("headphone", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("case", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("tower", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("speaker", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("webcam", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("printer", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("phone", StringComparison.OrdinalIgnoreCase);
        return productPath && QueryTokenRegex().Matches(title).Cast<Match>().Any(match => match.Value.Any(char.IsDigit));
    }

    private static bool CatalogResultMatchesQuery(string title, Uri uri, string query)
    {
        var normalizedQuery = NormalizeCompact(query);
        var haystack = NormalizeCompact($"{title} {Uri.UnescapeDataString(uri.AbsolutePath)}");
        if (normalizedQuery.Contains("keyboard", StringComparison.Ordinal))
            return haystack.Contains("keyboard", StringComparison.Ordinal) || haystack.Contains("keypad", StringComparison.Ordinal);
        if (normalizedQuery.Contains("mouse", StringComparison.Ordinal))
            return haystack.Contains("mouse", StringComparison.Ordinal) && !haystack.Contains("mousemat", StringComparison.Ordinal);
        if (normalizedQuery.Contains("monitor", StringComparison.Ordinal) || normalizedQuery.Contains("display", StringComparison.Ordinal))
            return haystack.Contains("monitor", StringComparison.Ordinal) || haystack.Contains("display", StringComparison.Ordinal);
        if (normalizedQuery.Contains("headset", StringComparison.Ordinal) || normalizedQuery.Contains("headphone", StringComparison.Ordinal))
            return haystack.Contains("headset", StringComparison.Ordinal) || haystack.Contains("headphone", StringComparison.Ordinal) || haystack.Contains("gamingaudio", StringComparison.Ordinal);
        if (normalizedQuery.Contains("chair", StringComparison.Ordinal))
            return haystack.Contains("chair", StringComparison.Ordinal);
        if (normalizedQuery.Contains("desk", StringComparison.Ordinal) || normalizedQuery.Contains("table", StringComparison.Ordinal))
            return haystack.Contains("desk", StringComparison.Ordinal) || haystack.Contains("table", StringComparison.Ordinal);
        if (normalizedQuery.Contains("case", StringComparison.Ordinal) || normalizedQuery.Contains("tower", StringComparison.Ordinal))
            return haystack.Contains("case", StringComparison.Ordinal) || haystack.Contains("tower", StringComparison.Ordinal);
        return true;
    }

    private static bool IsComplete(ExternalProductCandidateDto result) =>
        result.WidthCm is not null && result.HeightCm is not null && result.DepthCm is not null &&
        result.MeasurementStatus == "official-structured";

    private static IReadOnlyList<Uri> BuildSearchUrls(ProductIdentity identity)
    {
        var coreIdentity = identity.Model!.Length <= 3
            ? identity.SearchPhrase
            : $"{identity.Brand} {identity.Model}".Trim();
        var exact = $"\"{coreIdentity}\"";
        var specificationQuery = $"{exact} specifications dimensions";
        var datasheetQuery = $"{exact} datasheet filetype:pdf";
        var physicalQuery = $"{exact} product dimensions width height depth";
        var urls = new List<Uri>
        {
            new Uri($"https://www.bing.com/search?q={Uri.EscapeDataString(specificationQuery)}&setlang=en-US&cc=US"),
            new Uri($"https://www.google.com/search?q={Uri.EscapeDataString(specificationQuery)}&hl=en&num=10"),
            new Uri($"https://search.brave.com/search?q={Uri.EscapeDataString(specificationQuery)}&source=web"),
            new Uri($"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(specificationQuery)}"),
            new Uri($"https://www.mojeek.com/search?q={Uri.EscapeDataString(specificationQuery)}"),
            new Uri($"https://search.brave.com/search?q={Uri.EscapeDataString(datasheetQuery)}&source=web"),
            new Uri($"https://www.bing.com/search?q={Uri.EscapeDataString(physicalQuery)}&setlang=en-US&cc=US")
        };

        switch (NormalizeCompact(identity.Brand))
        {
            case "ikea":
                // IKEA's public product sitemap is split into locale-specific chunks. The
                // second English/Philippines chunk currently covers the broad MICKE range
                // and lets us discover canonical product pages without an account or API.
                urls.Insert(0, new Uri("https://www.ikea.com/sitemaps/prod-en-PH_2.xml"));
                break;
            case "asus":
                urls.Insert(0, new Uri("https://rog.asus.com/sitemap/product1.xml"));
                urls.Insert(1, new Uri($"https://rog.asus.com/search/?searchkeyword={Uri.EscapeDataString(identity.Model!)}"));
                urls.Insert(2, new Uri($"https://www.asus.com/searchresult?searchType=products&searchKey={Uri.EscapeDataString(identity.Model!)}"));
                break;
            case "samsung":
                urls.Insert(0, new Uri("https://www.samsung.com/uk/vd-sitemap.xml"));
                urls.Insert(1, new Uri("https://www.samsung.com/au/vd-sitemap.xml"));
                break;
            case "lg":
                urls.Insert(0, new Uri($"https://www.lg.com/us/search?q={Uri.EscapeDataString(identity.Model!)}"));
                break;
            case "dell":
                urls.Insert(0, new Uri($"https://www.dell.com/en-us/search/{Uri.EscapeDataString(identity.Model!)}"));
                break;
            case "logitech":
                urls.Insert(0, new Uri($"https://www.logitech.com/en-us/search.html?q={Uri.EscapeDataString(identity.SearchPhrase)}"));
                break;
        }

        return urls;
    }

    private static IReadOnlyList<Uri> BuildDirectManufacturerUrls(ProductIdentity identity, string query)
    {
        if (identity.Model is null) return [];
        var brand = NormalizeCompact(identity.Brand);
        var model = Uri.EscapeDataString(identity.Model.ToLowerInvariant());
        IReadOnlyList<Uri> urls = brand switch
        {
            "aoc" =>
            [
                new Uri($"https://www.aoc.com/us/gaming/monitors/{model}"),
                new Uri($"https://www.aoc.com/us/monitors/{model}")
            ],
            "msi" => BuildMsiUrls(identity),
            "tplink" => BuildTpLinkUrls(identity, query),
            _ => []
        };

        var result = urls.ToList();
        var slug = BuildProductSlug(query);
        if (query.Contains("mouse", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new Uri($"https://mousemetric.com/mice/{slug}"));
            result.Add(new Uri($"https://mousespecs.org/{slug}/"));
        }
        if (query.Contains("keyboard", StringComparison.OrdinalIgnoreCase))
            result.Add(new Uri($"https://betterbuybase.com/{slug}"));
        return result;
    }

    private static IReadOnlyList<Uri> BuildMsiUrls(ProductIdentity identity)
    {
        var slug = string.Join('-', identity.IdentityTokens.Where(token =>
            !token.Equals(identity.Brand, StringComparison.OrdinalIgnoreCase)));
        if (string.IsNullOrWhiteSpace(slug) || !slug.Contains(identity.Model!, StringComparison.OrdinalIgnoreCase))
            slug = identity.Model!;
        var slugs = new List<string> { slug, identity.Model! };
        if (!slug.Contains('-', StringComparison.Ordinal))
            slugs.AddRange(new[] { "MAG", "MPG", "MEG", "PRO", "Modern" }.Select(series => $"{series}-{identity.Model}"));

        return slugs
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(candidateSlug =>
            {
                var escapedSlug = Uri.EscapeDataString(candidateSlug);
                return new[]
                {
                    new Uri($"https://www.msi.com/Monitor/{escapedSlug}/Specification"),
                    new Uri($"https://storage-asset.msi.com/datasheet/monitor/global/{escapedSlug}.pdf"),
                    new Uri($"https://storage-asset.msi.com/datasheet/monitor/uk/{escapedSlug}.pdf")
                };
            })
            .ToList();
    }

    private static IReadOnlyList<Uri> BuildTpLinkUrls(ProductIdentity identity, string query)
    {
        var slug = string.Join('-', identity.IdentityTokens.Where(token =>
            !token.Equals(identity.Brand, StringComparison.OrdinalIgnoreCase)));
        if (string.IsNullOrWhiteSpace(slug) || !slug.Contains(identity.Model!, StringComparison.OrdinalIgnoreCase))
            slug = identity.Model!;
        var model = Uri.EscapeDataString(slug.ToLowerInvariant().Replace(' ', '-'));
        var paths = query.Contains("router", StringComparison.OrdinalIgnoreCase)
            ? new[] { "wifi-router", "dsl-modem-router", "vpn-router" }
            : new[] { "wifi-router", "range-extender", "access-point", "network-switch" };
        return paths.Select(path => new Uri($"https://www.tp-link.com/us/home-networking/{path}/{model}/")).ToList();
    }

    private static string BuildProductSlug(string query)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "monitor", "display", "mouse", "keyboard", "specification", "specifications", "dimension", "dimensions",
            "inch", "inches"
        };
        return string.Join('-', Regex.Split(query.ToLowerInvariant(), "[^a-z0-9]+")
            .Where(token => token.Length > 0 && !ignored.Contains(token)));
    }

    private async Task<ExternalProductCandidateDto?> ReadProductDocumentAsync(
        Uri sourceUri,
        ProductIdentity identity,
        CancellationToken cancellationToken)
    {
        if (IsUnsafeSourceUri(sourceUri) || IsDisallowedSourceHost(sourceUri.Host) ||
            HasUnrequestedVariantSuffix(sourceUri, identity))
            return null;
        try
        {
            using var response = await httpClient.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null || IsUnsafeSourceUri(finalUri)) return null;

            var isPdf = finalUri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                        response.Content.Headers.ContentType?.MediaType?.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) == true;
            if (isPdf)
                return await ReadPdfDocumentAsync(response.Content, finalUri, identity, cancellationToken);

            var html = await ReadLimitedStringAsync(response.Content, 3_000_000, cancellationToken);
            if (html is null || !ContainsIdentity(html, identity.Model!)) return null;

            var specifications = ExtractSpecifications(html);
            var measurements = ExtractMeasurements(specifications, html, identity.IsDisplay);
            if (!MeasurementsArePlausible(measurements, identity.IsDisplay)) return null;
            if (specifications.Count == 0 && measurements.WidthCm is null && measurements.HeightCm is null && measurements.DepthCm is null)
                return null;

            AddSpecification(specifications, "Kaynak", "Sayfa", finalUri.Host, null);
            AddSpecification(specifications, "Kimlik", "Model", identity.Model, null);
            AddSpecification(specifications, "Kimlik", "Marka", identity.Brand, null);
            AddSpecification(specifications, "Fiziksel", "Genişlik", measurements.WidthCm, "cm");
            AddSpecification(specifications, "Fiziksel", "Yükseklik", measurements.HeightCm, "cm");
            AddSpecification(specifications, "Fiziksel", "Derinlik", measurements.DepthCm, "cm");
            AddSpecification(specifications, "Fiziksel", "Ağırlık", measurements.WeightKg, "kg");

            var official = IsBrandHost(finalUri.Host, identity.Brand);
            var hasPhysicalMeasurements = measurements.WidthCm is not null || measurements.HeightCm is not null || measurements.DepthCm is not null;
            var title = identity.DisplayName;
            return new ExternalProductCandidateDto(
                official ? "Resmî üretici web kataloğu" : "Açık teknik ürün kataloğu",
                title,
                identity.Brand,
                identity.Model,
                identity.Model,
                null,
                measurements.WidthCm,
                measurements.HeightCm,
                measurements.DepthCm,
                measurements.WeightKg,
                new UriBuilder(finalUri) { Query = string.Empty, Fragment = string.Empty }.Uri.ToString(),
                official && hasPhysicalMeasurements ? "official-structured" : hasPhysicalMeasurements ? "source-structured" : "identity-only",
                official && hasPhysicalMeasurements
                    ? "Model ve ölçüler üreticinin resmî teknik sayfasından çıkarıldı."
                    : hasPhysicalMeasurements
                        ? "Model ve ölçüler açık teknik ürün sayfasındaki yapılandırılmış alanlardan çıkarıldı."
                        : "Ürün kimliği doğrulandı; bu sayfada güvenilir gövde ölçüsü yayımlanmamış.",
                specifications.Take(200).ToList());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Product source could not be read from {SourceUrl}", sourceUri);
            return null;
        }
    }

    // Reads one product page found in a manufacturer's own sitemap. Unlike ReadProductDocumentAsync there is
    // no search query to match against, so the identity comes from the page itself.
    public async Task<ExternalProductCandidateDto?> ReadManufacturerPageAsync(
        Uri pageUri,
        string brand,
        CancellationToken cancellationToken)
    {
        if (IsUnsafeSourceUri(pageUri)) return null;
        using var response = await httpClient.GetAsync(pageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Manufacturer site is rate limiting requests.", null, response.StatusCode);
        if (!response.IsSuccessStatusCode) return null;
        var finalUri = response.RequestMessage?.RequestUri ?? pageUri;
        if (IsUnsafeSourceUri(finalUri)) return null;

        var html = await ReadLimitedStringAsync(response.Content, 4_000_000, cancellationToken);
        return html is null ? null : ParseManufacturerPage(html, finalUri, brand);
    }

    // Shared by plain HTTP reads and headless-browser renders of a manufacturer product page.
    public static ExternalProductCandidateDto? ParseManufacturerPage(string html, Uri finalUri, string brand)
    {
        if (IsUnsafeSourceUri(finalUri)) return null;
        var title = ExtractPageProductName(html);
        if (string.IsNullOrWhiteSpace(title)) return null;

        var isDisplay = DisplayPageRegex().IsMatch($"{title} {finalUri.AbsolutePath}");
        var specifications = ExtractSpecifications(html);
        var measurements = ExtractMeasurements(specifications, html, isDisplay);
        if (measurements.WidthCm is null || measurements.HeightCm is null || measurements.DepthCm is null ||
            !MeasurementsArePlausible(measurements, isDisplay))
            return null;

        var displayName = ComposeDisplayName(brand, title);
        var model = displayName[brand.Length..].Trim();
        if (model.Length == 0) return null;

        AddSpecification(specifications, "Kaynak", "Sayfa", finalUri.Host, null);
        AddSpecification(specifications, "Kimlik", "Marka", brand, null);
        AddSpecification(specifications, "Kimlik", "Sayfa başlığı", ExtractTitle(html), null);
        AddSpecification(specifications, "Kimlik", "Sayfa açıklaması", MetaDescriptionRegex().Match(html) is { Success: true } description
            ? WebUtility.HtmlDecode(description.Groups["text"].Value)
            : null, null);
        AddSpecification(specifications, "Fiziksel", "Genişlik", measurements.WidthCm, "cm");
        AddSpecification(specifications, "Fiziksel", "Yükseklik", measurements.HeightCm, "cm");
        AddSpecification(specifications, "Fiziksel", "Derinlik", measurements.DepthCm, "cm");
        AddSpecification(specifications, "Fiziksel", "Ağırlık", measurements.WeightKg, "kg");

        var official = IsBrandHost(finalUri.Host, brand);
        return new ExternalProductCandidateDto(
            official ? "Resmî üretici web kataloğu" : "Açık teknik ürün kataloğu",
            displayName,
            brand,
            model,
            null,
            null,
            measurements.WidthCm,
            measurements.HeightCm,
            measurements.DepthCm,
            measurements.WeightKg,
            new UriBuilder(finalUri) { Query = string.Empty, Fragment = string.Empty }.Uri.ToString(),
            official ? "official-structured" : "source-structured",
            "Ölçüler üreticinin ürün haritasında yayımlanan teknik sayfadan çıkarıldı.",
            specifications.Take(200).ToList());
    }

    // "2021 ASUS TUF Gaming F15" → "ASUS TUF Gaming F15 (2021)": the model year moves behind the name (it still
    // tells generations apart) and the brand goes to the front instead of being repeated.
    internal static string ComposeDisplayName(string brand, string title)
    {
        var yearPrefix = Regex.Match(title, "^(?<year>(?:19|20)\\d{2})\\s+", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        var name = (yearPrefix.Success ? $"{title[yearPrefix.Length..].Trim()} ({yearPrefix.Groups["year"].Value})" : title).Trim();
        if (name.StartsWith(brand, StringComparison.OrdinalIgnoreCase)) return name;
        var brandIndex = name.IndexOf($" {brand} ", StringComparison.OrdinalIgnoreCase);
        if (brandIndex >= 0) name = (name[..brandIndex] + " " + name[(brandIndex + brand.Length + 2)..]).Trim();
        return $"{brand} {name}";
    }

    // og:title is usually the clean product name; <title> carries " | Brand" or "Buy ..." decorations.
    internal static string? ExtractPageProductName(string html)
    {
        var raw = OpenGraphTitleRegex().Match(html) is { Success: true } og
            ? og.Groups["title"].Value
            : ExtractTitle(html);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var name = WebUtility.HtmlDecode(raw).Split([" | ", " – ", " — ", "｜"], StringSplitOptions.TrimEntries)[0];
        name = Regex.Replace(name, "^(?:Buy|Shop)\\s+|[™®©]", string.Empty, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)).Trim();
        // Variant and region decorations: "Viper V3 Pro - Sentinels Edition", "Apex Pro Gen 3 - US", "Apex 3 US".
        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 4) name = name[..dash].Trim();
        name = Regex.Replace(name, "\\s+(?:US|UK|EU|Nordic|International)$", string.Empty, RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();
        return name.Length is >= 3 and <= 200 ? name : null;
    }

    private async Task<ExternalProductCandidateDto?> ReadPdfDocumentAsync(
        HttpContent content,
        Uri sourceUri,
        ProductIdentity identity,
        CancellationToken cancellationToken)
    {
        const int maxPdfBytes = 12_000_000;
        if (content.Headers.ContentLength is > maxPdfBytes) return null;
        var bytes = await ReadLimitedBytesAsync(content, maxPdfBytes, cancellationToken);
        if (bytes is null) return null;

        using var document = PdfDocument.Open(bytes);
        var text = new StringBuilder();
        foreach (var page in document.GetPages().Take(80))
        {
            text.AppendLine(ContentOrderTextExtractor.GetText(page));
            if (text.Length > 3_000_000) break;
        }

        var documentText = text.ToString();
        if (!ContainsIdentity(documentText, identity.Model!)) return null;

        var specifications = ExtractTextSpecifications(documentText);
        var measurements = ExtractMeasurements(specifications, documentText, identity.IsDisplay);
        if (!MeasurementsArePlausible(measurements, identity.IsDisplay)) return null;
        if (measurements.WidthCm is null && measurements.HeightCm is null && measurements.DepthCm is null)
            return null;

        AddSpecification(specifications, "Kaynak", "Belge", sourceUri.Host, null);
        AddSpecification(specifications, "Kimlik", "Model", identity.Model, null);
        AddSpecification(specifications, "Kimlik", "Marka", identity.Brand, null);
        AddSpecification(specifications, "Fiziksel", "Genişlik", measurements.WidthCm, "cm");
        AddSpecification(specifications, "Fiziksel", "Yükseklik", measurements.HeightCm, "cm");
        AddSpecification(specifications, "Fiziksel", "Derinlik", measurements.DepthCm, "cm");
        AddSpecification(specifications, "Fiziksel", "Ağırlık", measurements.WeightKg, "kg");

        var official = IsBrandHost(sourceUri.Host, identity.Brand);
        return new ExternalProductCandidateDto(
            official ? "Resmî üretici teknik föyü" : "Açık teknik ürün belgesi",
            identity.DisplayName,
            identity.Brand,
            identity.Model,
            identity.Model,
            null,
            measurements.WidthCm,
            measurements.HeightCm,
            measurements.DepthCm,
            measurements.WeightKg,
            new UriBuilder(sourceUri) { Query = string.Empty, Fragment = string.Empty }.Uri.ToString(),
            official ? "official-structured" : "source-structured",
            official
                ? "Ölçüler üreticinin resmî teknik föyünden çıkarıldı."
                : "Ölçüler herkese açık teknik ürün belgesinden çıkarıldı.",
            specifications.Take(200).ToList());
    }

    private static List<ExternalSpecificationDto> ExtractTextSpecifications(string text)
    {
        var specifications = new List<ExternalSpecificationDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TextDimensionSpecificationRegex().Matches(text))
            AddSpecification(specifications, seen, "Teknik föy", match.Groups["name"].Value, match.Groups["value"].Value, null);
        foreach (Match match in TextWeightSpecificationRegex().Matches(text))
            AddSpecification(specifications, seen, "Teknik föy", match.Groups["name"].Value, match.Groups["value"].Value, null);
        foreach (Match match in TextFeatureSpecificationRegex().Matches(text))
            AddSpecification(specifications, seen, "Teknik föy", match.Groups["name"].Value, match.Groups["value"].Value, null);
        return specifications;
    }

    private static bool ContainsIdentity(string value, string identity) =>
        NormalizeCompact(value).Contains(NormalizeCompact(identity), StringComparison.OrdinalIgnoreCase);

    private static bool MeasurementsArePlausible(MeasurementSet measurements, bool isDisplay)
    {
        var dimensions = new[] { measurements.WidthCm, measurements.HeightCm, measurements.DepthCm };
        if (dimensions.Any(value => value is <= 0.05m or > 500m)) return false;
        if (measurements.WeightKg is <= 0.005m or > 500m) return false;
        if (!isDisplay || dimensions.Any(value => value is null)) return true;
        return measurements.WidthCm is >= 15m and <= 300m &&
               measurements.HeightCm is >= 8m and <= 200m &&
               measurements.DepthCm is >= 1m and <= 150m;
    }

    private static bool HasUnrequestedVariantSuffix(Uri sourceUri, ProductIdentity identity)
    {
        var pathTokens = Regex.Matches(Uri.UnescapeDataString(sourceUri.AbsolutePath), "[\\p{L}\\p{Nd}]+")
            .Select(match => match.Value)
            .ToArray();
        var modelIndex = Array.FindIndex(pathTokens, token => token.Equals(identity.Model, StringComparison.OrdinalIgnoreCase));
        if (modelIndex < 0 || modelIndex + 1 >= pathTokens.Length) return false;
        var suffix = pathTokens[modelIndex + 1];
        if (identity.IdentityTokens.Contains(suffix, StringComparer.OrdinalIgnoreCase)) return false;
        return suffix.Any(char.IsLetter) && suffix.Any(char.IsDigit);
    }

    private static IReadOnlyList<Uri> ExtractResultUrls(string html, ProductIdentity identity)
    {
        var decoded = WebUtility.HtmlDecode(html);
        var urls = new List<Uri>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sitemapUrl in ExtractMatchingSitemapUrls(decoded, identity.Model!))
        {
            if (seen.Add(sitemapUrl.AbsoluteUri)) urls.Add(sitemapUrl);
        }
        // Product sitemap chunks can be tens of megabytes. Once the matching <loc>
        // entries have been found, avoid running every general-purpose link regex over
        // the whole document.
        if (urls.Count > 0 && decoded.Contains("<urlset", StringComparison.OrdinalIgnoreCase))
            return urls;
        var resultLinks = SearchResultLinkRegex().Matches(decoded).Cast<Match>()
            .Concat(SearchResultLinkClassFirstRegex().Matches(decoded).Cast<Match>())
            .Concat(BingResultLinkRegex().Matches(decoded).Cast<Match>())
            .Concat(AllAnchorLinkRegex().Matches(decoded).Cast<Match>())
            .Concat(SitemapLocationRegex().Matches(decoded).Cast<Match>());
        foreach (var match in resultLinks)
        {
            var rawUrl = match.Groups["url"].Value;
            if (rawUrl.StartsWith("//", StringComparison.Ordinal)) rawUrl = $"https:{rawUrl}";
            var uri = ResolveSearchResultUri(rawUrl);
            if (uri is null || IsSearchHost(uri.Host)) continue;
            var clean = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri;
            if (seen.Add(clean.AbsoluteUri)) urls.Add(clean);
        }
        return urls;
    }

    private static IEnumerable<Uri> ExtractMatchingSitemapUrls(string content, string model)
    {
        if (!content.Contains("<loc>", StringComparison.OrdinalIgnoreCase)) yield break;
        var cursor = 0;
        while (cursor < content.Length)
        {
            var start = content.IndexOf("<loc>", cursor, StringComparison.OrdinalIgnoreCase);
            if (start < 0) yield break;
            var end = content.IndexOf("</loc>", start + 5, StringComparison.OrdinalIgnoreCase);
            if (end < 0) yield break;
            cursor = end + 6;
            if (end - start > 2_000) continue;
            var rawUrl = WebUtility.HtmlDecode(content[(start + 5)..end].Trim());
            if (!rawUrl.Contains(model, StringComparison.OrdinalIgnoreCase)) continue;
            if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                yield return uri;
        }
    }

    private static Uri? ResolveSearchResultUri(string rawUrl)
    {
        if (rawUrl.StartsWith("/url?", StringComparison.OrdinalIgnoreCase))
        {
            var query = rawUrl[(rawUrl.IndexOf('?') + 1)..];
            var target = GetQueryValue(query, "q") ?? GetQueryValue(query, "url");
            return target is not null && Uri.TryCreate(Uri.UnescapeDataString(target), UriKind.Absolute, out var googleTarget) &&
                   googleTarget.Scheme == Uri.UriSchemeHttps ? googleTarget : null;
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            var encodedTarget = GetQueryValue(uri.Query, "uddg");
            return encodedTarget is not null && Uri.TryCreate(Uri.UnescapeDataString(encodedTarget), UriKind.Absolute, out var duckTarget) &&
                   duckTarget.Scheme == Uri.UriSchemeHttps ? duckTarget : null;
        }

        if (uri.Host.EndsWith("bing.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith("/ck/", StringComparison.OrdinalIgnoreCase))
        {
            var encodedTarget = GetQueryValue(WebUtility.HtmlDecode(uri.Query), "u");
            if (encodedTarget is null) return null;
            encodedTarget = Uri.UnescapeDataString(encodedTarget);
            if (encodedTarget.StartsWith("a1", StringComparison.Ordinal)) encodedTarget = encodedTarget[2..];
            try
            {
                encodedTarget = encodedTarget.Replace('-', '+').Replace('_', '/');
                encodedTarget = encodedTarget.PadRight(encodedTarget.Length + ((4 - encodedTarget.Length % 4) % 4), '=');
                var target = Encoding.UTF8.GetString(Convert.FromBase64String(encodedTarget));
                return Uri.TryCreate(target, UriKind.Absolute, out var bingTarget) && bingTarget.Scheme == Uri.UriSchemeHttps
                    ? bingTarget
                    : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return uri;
    }

    private static bool IsSearchHost(string host) =>
        host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("bing.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("brave.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("mojeek.com", StringComparison.OrdinalIgnoreCase);

    private static ExternalProductCandidateDto? ReadSearchResultSnippets(
        string searchHtml,
        Uri searchUri,
        ProductIdentity identity)
    {
        var visibleText = CleanHtml(searchHtml);
        if (!ContainsIdentity(visibleText, identity.Model!)) return null;

        var contexts = ExtractIdentityContexts(visibleText, identity.Model!);
        foreach (var context in contexts)
        {
            var specifications = ExtractTextSpecifications(context);
            var measurements = ExtractMeasurements(specifications, context, identity.IsDisplay);
            if (measurements.WidthCm is null || measurements.HeightCm is null || measurements.DepthCm is null)
                continue;
            if (!MeasurementsArePlausible(measurements, identity.IsDisplay)) continue;

            AddSpecification(specifications, "Kimlik", "Model", identity.Model, null);
            AddSpecification(specifications, "Kimlik", "Marka", identity.Brand, null);
            AddSpecification(specifications, "Fiziksel", "Genişlik", measurements.WidthCm, "cm");
            AddSpecification(specifications, "Fiziksel", "Yükseklik", measurements.HeightCm, "cm");
            AddSpecification(specifications, "Fiziksel", "Derinlik", measurements.DepthCm, "cm");
            return new ExternalProductCandidateDto(
                "Web teknik sonuç özeti",
                identity.DisplayName,
                identity.Brand,
                identity.Model,
                identity.Model,
                null,
                measurements.WidthCm,
                measurements.HeightCm,
                measurements.DepthCm,
                measurements.WeightKg,
                searchUri.ToString(),
                "source-structured",
                "Üretici sayfası erişimi engellediği için ölçüler teknik arama sonucundaki yayımlanmış özetten çıkarıldı.",
                specifications.Take(40).ToList());
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractIdentityContexts(string text, string model)
    {
        var contexts = new List<string>();
        var startAt = 0;
        while (contexts.Count < 12)
        {
            var index = text.IndexOf(model, startAt, StringComparison.OrdinalIgnoreCase);
            if (index < 0) break;
            var start = Math.Max(0, index - 700);
            contexts.Add(text.Substring(start, Math.Min(2_100, text.Length - start)));
            startAt = index + model.Length;
        }
        return contexts;
    }

    private static string? GetQueryValue(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals(name, StringComparison.OrdinalIgnoreCase)) return parts[1];
        }
        return null;
    }

    private static int ScoreSource(Uri uri, ProductIdentity identity)
    {
        if (IsDisallowedSourceHost(uri.Host)) return -1_000;
        var score = 0;
        if (IsBrandHost(uri.Host, identity.Brand)) score += 250;
        if (TrustedSpecificationHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase))) score += 150;
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (ContainsIdentity(path, identity.Model!)) score += 700;
        foreach (var token in QueryTokenRegex().Matches(identity.SearchPhrase).Select(match => match.Value)
                     .Where(token => token.Length >= 3 && !token.Equals(identity.Brand, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ContainsIdentity(path, token)) score += 40;
            if (!token.Equals(identity.Model, StringComparison.OrdinalIgnoreCase) &&
                path.Contains($"{identity.Model}-{token}", StringComparison.OrdinalIgnoreCase))
                score += 140;
        }
        foreach (var variant in new[] { "drawer", "add-on", "combination", "workstation", "chair" })
        {
            if (path.Contains(variant, StringComparison.OrdinalIgnoreCase) &&
                !identity.SearchPhrase.Contains(variant, StringComparison.OrdinalIgnoreCase))
                score -= 180;
        }
        if (Regex.IsMatch(path, "-s\\d+/?$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)) &&
            !identity.SearchPhrase.Contains("set", StringComparison.OrdinalIgnoreCase) &&
            !identity.SearchPhrase.Contains("combination", StringComparison.OrdinalIgnoreCase))
            score -= 160;
        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) score += 120;
        if (uri.AbsolutePath.Contains("spec", StringComparison.OrdinalIgnoreCase)) score += 50;
        if (uri.AbsolutePath.Contains("product", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Contains("monitor", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (uri.Host.Contains("amazon.", StringComparison.OrdinalIgnoreCase) || uri.Host.Contains("ebay.", StringComparison.OrdinalIgnoreCase)) score -= 200;
        if (HasUnrequestedVariantSuffix(uri, identity)) score -= 500;
        return score;
    }

    private static bool IsDisallowedSourceHost(string host) =>
        DisallowedSourceHosts.Any(domain => HostMatchesDomain(host, domain));

    private static bool IsUnsafeSourceUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) ||
            (!uri.IsDefaultPort && uri.Port != 443)) return true;
        var host = uri.IdnHost;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return address.Equals(IPAddress.IPv6Loopback) || address.IsIPv6LinkLocal || address.IsIPv6Multicast ||
                   address.IsIPv6SiteLocal || (address.GetAddressBytes()[0] & 0xfe) == 0xfc;
        var bytes = address.GetAddressBytes();
        return bytes[0] is 0 or 10 or 127 ||
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               bytes[0] >= 224;
    }

    private static int QualityScore(ExternalProductCandidateDto result) =>
        (result.MeasurementStatus == "official-structured" ? 240 : 0) +
        (result.WidthCm is not null ? 120 : 0) +
        (result.HeightCm is not null ? 120 : 0) +
        (result.DepthCm is not null ? 120 : 0) +
        result.Specifications.Count;

    // True when the visible text just before a spec row is about the parcel ("Packaging", "Shipping"),
    // so per-parcel width/height rows are not mistaken for the product body.
    private static bool InPackagingContext(string html, int index)
    {
        var start = Math.Max(0, index - 1_500);
        var preceding = CleanHtml(html[start..index]);
        return PackagingTermsRegex().IsMatch(preceding[Math.Max(0, preceding.Length - 250)..]);
    }

    private static List<ExternalSpecificationDto> ExtractSpecifications(string html)
    {
        var specifications = new List<ExternalSpecificationDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in LabelValueBlockRegex().Matches(html))
            AddSpecification(specifications, seen, "Üretici teknik özellikleri", CleanHtml(match.Groups["name"].Value), CleanHtml(match.Groups["value"].Value), null);

        foreach (Match row in TableRowRegex().Matches(html))
        {
            if (InPackagingContext(html, row.Index)) continue;
            var cells = TableCellRegex().Matches(row.Groups["row"].Value)
                .Select(cell => CleanHtml(cell.Groups["cell"].Value))
                .Where(cell => cell.Length > 0)
                .Take(3)
                .ToArray();
            if (cells.Length >= 2) AddSpecification(specifications, seen, "Teknik tablo", cells[0], string.Join(" · ", cells.Skip(1)), null);
        }

        foreach (Match pair in DefinitionPairRegex().Matches(html).Where(pair => !InPackagingContext(html, pair.Index)))
            AddSpecification(specifications, seen, "Teknik ayrıntılar", CleanHtml(pair.Groups["name"].Value), CleanHtml(pair.Groups["value"].Value), null);

        foreach (Match pair in MeasurementSpanPairRegex().Matches(html).Where(pair => !InPackagingContext(html, pair.Index)))
            AddSpecification(specifications, seen, "Üretici ölçüleri", CleanHtml(pair.Groups["name"].Value), CleanHtml(pair.Groups["value"].Value), null);

        foreach (Match pair in JsonMeasurementNameFirstRegex().Matches(html).Cast<Match>()
                     .Concat(JsonMeasurementValueFirstRegex().Matches(html).Cast<Match>()))
            AddSpecification(specifications, seen, "Yapılandırılmış ölçüler", pair.Groups["name"].Value, pair.Groups["value"].Value, null);

        // Storefront bundles (Next.js, SFCC, Vue) embed spec lists as object literals, often with unquoted keys:
        // {facet:"Height",value:"1.62 in (41.1 mm)"}.
        foreach (Match pair in ScriptLabelValueRegex().Matches(html))
            AddSpecification(specifications, seen, "Yapılandırılmış ölçüler", pair.Groups["name"].Value, pair.Groups["value"].Value, null);

        // Spec lists rendered as text, also inside JSON-escaped HTML: "<li>Length: 127.1 mm / 5.00 in</li>"
        // (Razer) or "Length|||120.55mm / 4.75 inches" (SteelSeries).
        // Tags become line breaks so each list item stays on its own line for the label look-behind.
        var decodedText = WebUtility.HtmlDecode(HtmlTagRegex().Replace(JsonUnicodeEscapeRegex().Replace(html, match =>
            ((char)Convert.ToInt32(match.Groups["hex"].Value, 16)).ToString()), "\n"));
        foreach (Match axis in TextAxisMeasurementRegex().Matches(decodedText))
        {
            // Parcel sizes are listed with the same labels ("Packaging … Height: 3 cm"); skip them.
            var context = decodedText.Substring(Math.Max(0, axis.Index - 250), Math.Min(250, axis.Index));
            if (PackagingTermsRegex().IsMatch(context)) continue;
            var name = WhitespaceRegex().Replace(axis.Groups["name"].Value, " ");
            AddSpecification(specifications, seen, "Üretici ölçü listesi", name, axis.Groups["value"].Value.Trim(), null);
        }

        foreach (var card in ExtractCardSpecifications(html))
            AddSpecification(specifications, seen, "Teknik kartlar", card.Name, card.Value, null);

        foreach (Match property in JsonPropertyValueRegex().Matches(html))
            AddSpecification(
                specifications,
                seen,
                "Yapılandırılmış ürün verisi",
                WebUtility.HtmlDecode(property.Groups["name"].Value),
                WebUtility.HtmlDecode(property.Groups["value"].Value),
                WebUtility.HtmlDecode(property.Groups["unit"].Value));

        return specifications.Take(200).ToList();
    }

    private static IEnumerable<(string Name, string Value)> ExtractCardSpecifications(string html)
    {
        const string cardMarker = "<div class=\"cmp-card\"";
        const string titleMarker = "cmp-card-title";
        const string valueMarker = "cmp-card-val";
        var start = 0;
        while ((start = html.IndexOf(cardMarker, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var next = html.IndexOf(cardMarker, start + cardMarker.Length, StringComparison.OrdinalIgnoreCase);
            var length = (next < 0 ? html.Length : next) - start;
            var card = html.Substring(start, Math.Min(length, 5_000));
            var titleClass = card.IndexOf(titleMarker, StringComparison.OrdinalIgnoreCase);
            var valueClass = card.IndexOf(valueMarker, StringComparison.OrdinalIgnoreCase);
            if (titleClass >= 0 && valueClass >= 0)
            {
                var titleOpen = card.IndexOf('>', titleClass);
                var titleClose = titleOpen < 0 ? -1 : card.IndexOf("</div>", titleOpen, StringComparison.OrdinalIgnoreCase);
                var valueOpen = card.IndexOf('>', valueClass);
                var valueClose = valueOpen < 0 ? -1 : card.IndexOf("</span>", valueOpen, StringComparison.OrdinalIgnoreCase);
                if (titleOpen >= 0 && titleClose > titleOpen && valueOpen >= 0 && valueClose > valueOpen)
                    yield return (CleanHtml(card[(titleOpen + 1)..titleClose]), CleanHtml(card[(valueOpen + 1)..valueClose]));
            }
            if (next < 0) yield break;
            start = next;
        }
    }

    private static MeasurementSet ExtractMeasurements(
        IReadOnlyList<ExternalSpecificationDto> specifications,
        string html,
        bool isDisplay)
    {
        var dimension = specifications
            .Where(specification => IsProductDimension(specification.Name, specification.Group))
            .OrderByDescending(specification => DimensionPriority(specification.Name))
            .Select(specification => ParseDimensionTriple(specification.Name, specification.Value, isDisplay))
            .FirstOrDefault(value => value is not null);

        var width = dimension?.WidthCm ?? FindNamedMeasurement(specifications, ["width", "product width", "genişlik"], "cm");
        // Sit/stand desks publish only a range; the lowest setting is the seated working height.
        var height = dimension?.HeightCm ?? FindNamedMeasurement(specifications, ["height", "product height", "front height", "rear height", "yükseklik", "min. height", "minimum height", "max. height", "maximum height"], "cm");
        var depth = dimension?.DepthCm ?? FindNamedMeasurement(specifications, ["depth", "product depth", "length", "derinlik"], "cm");
        var weight = FindWeightMeasurement(specifications);

        if (width is null || height is null || depth is null)
        {
            // The first "dimensions" on a page is often the package or a script blob; use the first product triple.
            var text = CleanHtml(html);
            dimension = GenericDimensionRegex().Matches(text)
                .Where(match => !PackagingTermsRegex().IsMatch(match.Groups["label"].Value))
                .Select(match => ParseDimensionTriple(match.Groups["label"].Value, match.Groups["value"].Value, isDisplay))
                .FirstOrDefault(parsed => parsed is not null);
            width ??= dimension?.WidthCm;
            height ??= dimension?.HeightCm;
            depth ??= dimension?.DepthCm;
        }
        return new MeasurementSet(width, height, depth, weight);
    }

    private static bool IsProductDimension(string name, string group)
    {
        var value = $"{group} {name}";
        return value.Contains("dimension", StringComparison.OrdinalIgnoreCase) &&
               !PackagingTermsRegex().IsMatch(value) &&
               !value.Contains("excl", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("without", StringComparison.OrdinalIgnoreCase);
    }

    private static int DimensionPriority(string name)
    {
        var score = 0;
        if (name.Contains("incl", StringComparison.OrdinalIgnoreCase) || name.Contains("with stand", StringComparison.OrdinalIgnoreCase) || name.Contains("base", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (name.Contains("product", StringComparison.OrdinalIgnoreCase)) score += 30;
        return score;
    }

    private static MeasurementSet? ParseDimensionTriple(string label, string value, bool isDisplay)
    {
        var match = DimensionTripleRegex().Match(value);
        if (!match.Success) return null;
        var unit = match.Groups["unit"].Success ? match.Groups["unit"].Value : UnitInLabelRegex().Match(label).Groups["unit"].Value;
        if (string.IsNullOrWhiteSpace(unit)) return null;
        var values = new[] { "a", "b", "c" }
            .Select(name => ParseRangeMaximum(match.Groups[name].Value))
            .Select(number => number is null ? (decimal?)null : ToCentimeters(number.Value, unit))
            .ToArray();
        if (values.Any(number => number is null)) return null;
        var dimensions = values.Select(number => number!.Value).ToArray();

        if (isDisplay)
        {
            Array.Sort(dimensions);
            return new MeasurementSet(dimensions[2], dimensions[1], dimensions[0], null);
        }

        var order = NormalizeCompact(label.Replace('×', 'x'));
        if (order.Contains("wxdxh")) return new MeasurementSet(dimensions[0], dimensions[2], dimensions[1], null);
        if (order.Contains("wxhxd")) return new MeasurementSet(dimensions[0], dimensions[1], dimensions[2], null);
        if (order.Contains("lxwxh")) return new MeasurementSet(dimensions[1], dimensions[2], dimensions[0], null);
        // When a source publishes an unlabeled triple, the two largest values are
        // the surface footprint and the smallest value is normally product height
        // (common L×W×H / W×D×H catalog ordering). Rotation makes width/depth
        // interchangeable for fit checks, but treating the middle value as height
        // would produce dangerously small footprints for laptops and flat devices.
        return new MeasurementSet(dimensions.Max(), dimensions.Min(), dimensions.Order().ElementAt(1), null);
    }

    // "1.62 in (41.1 mm)" mixes systems; reading the largest number with the first unit would yield 41.1 in.
    // When several metric values are listed ("57.91mm (front), 67.03mm (back)") the largest is the body outline.
    internal static string PreferMetricValue(string value)
    {
        var metrics = MetricMeasurementRegex().Matches(value);
        if (metrics.Count == 0 || (metrics.Count == 1 && !ImperialMeasurementRegex().IsMatch(value))) return value;
        var largest = metrics
            .Select(match => (Match: match, Millimetres: decimal.Parse(match.Groups["number"].Value.Replace(',', '.'), CultureInfo.InvariantCulture) *
                (match.Groups["unit"].Value.Equals("cm", StringComparison.OrdinalIgnoreCase) ? 10 : 1)))
            .MaxBy(item => item.Millimetres)
            .Match;
        return $"{largest.Groups["number"].Value} {largest.Groups["unit"].Value}";
    }

    // Development probe aid: every specification the readers extracted, with the reader that found it.
    internal static IReadOnlyList<string> DescribeSpecifications(string html) =>
        ExtractSpecifications(html).Select(spec => $"{spec.Group} | {spec.Name} = {spec.Value}").ToList();

    // Development probe aid: the text around each "dimension" mention and what the triple reader made of it.
    internal static IReadOnlyList<string> DescribeDimensionText(string html)
    {
        var text = CleanHtml(html);
        var contexts = Regex.Matches(text, "dimension", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
            .Take(8)
            .Select(match => "text: " + text.Substring(Math.Max(0, match.Index - 20), Math.Min(140, text.Length - Math.Max(0, match.Index - 20))));
        var triples = GenericDimensionRegex().Matches(text)
            .Take(8)
            .Select(match => $"triple: [{match.Groups["label"].Value}] [{match.Groups["value"].Value}]");
        return contexts.Concat(triples).ToList();
    }

    internal static (decimal? WidthCm, decimal? HeightCm, decimal? DepthCm) ReadBodyDimensions(string html, bool isDisplay = false)
    {
        var measurements = ExtractMeasurements(ExtractSpecifications(html), html, isDisplay);
        return (measurements.WidthCm, measurements.HeightCm, measurements.DepthCm);
    }

    private static decimal? FindNamedMeasurement(
        IReadOnlyList<ExternalSpecificationDto> specifications,
        IReadOnlyList<string> preferredNames,
        string targetUnit)
    {
        foreach (var preferred in preferredNames)
        {
            var matches = specifications
                .Where(specification =>
                    SpecificationNameMatches(specification.Name, preferred) &&
                    !PackagingTermsRegex().IsMatch($"{specification.Group} {specification.Name}"))
                .OrderByDescending(MeasurementCandidatePriority);
            foreach (var match in matches)
            {
                var value = PreferMetricValue(match.Value);
                var unit = value == match.Value ? match.Unit ?? UnitInLabelRegex().Match(match.Name).Groups["unit"].Value : null;
                var number = ParseRangeMaximum(value);
                if (number is null) continue;
                if (string.IsNullOrWhiteSpace(unit)) unit = UnitInLabelRegex().Match(value).Groups["unit"].Value;
                if (string.IsNullOrWhiteSpace(unit)) unit = targetUnit;
                return targetUnit == "kg" ? ToKilograms(number.Value, unit) : ToCentimeters(number.Value, unit);
            }
        }
        return null;
    }

    private static int MeasurementNamePriority(string name)
    {
        if (name.Contains("with stand", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("incl. stand", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("incl stand", StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Contains("without stand", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("excl", StringComparison.OrdinalIgnoreCase)) return 10;
        return 50;
    }

    private static int MeasurementCandidatePriority(ExternalSpecificationDto specification) =>
        MeasurementNamePriority(specification.Name) +
        (!string.IsNullOrWhiteSpace(specification.Unit) || UnitInLabelRegex().IsMatch(specification.Value) ? 200 : 0);

    private static decimal? FindWeightMeasurement(IReadOnlyList<ExternalSpecificationDto> specifications)
    {
        var candidates = specifications
            .Where(specification => specification.Name.Contains("weight", StringComparison.OrdinalIgnoreCase))
            .Where(specification =>
                !specification.Name.Contains("gross", StringComparison.OrdinalIgnoreCase) &&
                (!PackagingTermsRegex().IsMatch($"{specification.Group} {specification.Name}") ||
                 specification.Name.Contains("net", StringComparison.OrdinalIgnoreCase) ||
                 specification.Name.Contains("excl", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(specification => WeightPriority(specification.Name));

        foreach (var candidate in candidates)
        {
            var unit = candidate.Unit ?? UnitInLabelRegex().Match(candidate.Name).Groups["unit"].Value;
            var number = ParseRangeMaximum(candidate.Value);
            if (number is null) continue;
            if (string.IsNullOrWhiteSpace(unit)) unit = "kg";
            return ToKilograms(number.Value, unit);
        }

        return null;
    }

    private static int WeightPriority(string name)
    {
        var score = 0;
        if (name.Contains("net", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (name.Contains("excl", StringComparison.OrdinalIgnoreCase)) score += 80;
        if (name.Contains("with stand", StringComparison.OrdinalIgnoreCase)) score += 60;
        if (name.Contains("product", StringComparison.OrdinalIgnoreCase)) score += 30;
        return score;
    }

    private static bool SpecificationNameMatches(string name, string preferred)
    {
        var trimmed = name.Trim().TrimEnd('?').Trim();
        if (trimmed.Equals(preferred, StringComparison.OrdinalIgnoreCase)) return true;
        if (!trimmed.StartsWith(preferred, StringComparison.OrdinalIgnoreCase)) return false;
        var suffix = trimmed[preferred.Length..].Trim();
        if (!suffix.StartsWith('(') || !suffix.EndsWith(')')) return false;
        return UnitInLabelRegex().IsMatch(suffix) ||
               suffix.Contains("with stand", StringComparison.OrdinalIgnoreCase) ||
               suffix.Contains("without stand", StringComparison.OrdinalIgnoreCase) ||
               suffix.Contains("incl. stand", StringComparison.OrdinalIgnoreCase) ||
               suffix.Contains("incl stand", StringComparison.OrdinalIgnoreCase);
    }

    private static ProductIdentity ParseIdentity(string query)
    {
        var decodedQuery = WebUtility.HtmlDecode(query);
        var tokens = QueryTokenRegex().Matches(decodedQuery)
            .Select(match => match.Value.Trim('-', '_'))
            .Where(token => token.Length > 0)
            .ToArray();
        var brand = DetectBrand(tokens);
        var model = tokens
            .Where(token => token.Any(char.IsLetter) && token.Any(char.IsDigit) && token.Length >= 2)
            .Where(token => !LooksLikeScreenSize(token))
            .OrderByDescending(token => token.Length)
            .FirstOrDefault();
        if (model is null)
        {
            for (var index = 1; index < tokens.Length; index++)
            {
                var previous = tokens[index - 1];
                var current = tokens[index];
                if (!current.All(char.IsDigit) || current.Length > 4 || previous.All(char.IsDigit) ||
                    SearchNoiseTerms.Contains(previous) || previous.Equals(brand, StringComparison.OrdinalIgnoreCase)) continue;
                model = $"{previous} {current}";
                break;
            }
        }
        model ??= tokens
            .Where(token => token.Any(char.IsLetter) && !SearchNoiseTerms.Contains(token))
            .Where(token => !token.Equals(brand, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(token => token.Length)
            .FirstOrDefault();
        var brandParts = brand is null
            ? []
            : QueryTokenRegex().Matches(brand).Select(match => match.Value).ToArray();
        var identityTokens = tokens
            .Where(token => !SearchNoiseTerms.Contains(token))
            .Where(token => !brandParts.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Where(token => !token.All(char.IsDigit) || token.Length >= 5 ||
                            (model?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(token, StringComparer.OrdinalIgnoreCase) ?? false))
            .Where(token => !LooksLikeScreenSize(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (brand is not null)
            identityTokens = [brand, .. identityTokens];
        if (model is not null && !identityTokens.Contains(model, StringComparer.OrdinalIgnoreCase))
            identityTokens = [.. identityTokens, model];

        var displayName = string.Join(' ', identityTokens);
        if (string.IsNullOrWhiteSpace(displayName)) displayName = $"{brand} {model}".Trim();
        var searchPhrase = string.Join(' ', tokens.Take(8));
        if (string.IsNullOrWhiteSpace(searchPhrase)) searchPhrase = displayName;
        var isDisplay = query.Contains("monitor", StringComparison.OrdinalIgnoreCase) ||
                        query.Contains("display", StringComparison.OrdinalIgnoreCase) ||
                        query.Contains("screen", StringComparison.OrdinalIgnoreCase) ||
                        query.Contains("ekran", StringComparison.OrdinalIgnoreCase) ||
                        query.Contains("monitör", StringComparison.OrdinalIgnoreCase);
        return new ProductIdentity(brand, model, displayName, searchPhrase, identityTokens, isDisplay);
    }

    private static string? DetectBrand(IReadOnlyList<string> tokens)
    {
        for (var length = Math.Min(3, tokens.Count); length >= 1; length--)
        {
            for (var start = 0; start + length <= tokens.Count; start++)
            {
                var candidateTokens = tokens.Skip(start).Take(length).ToArray();
                if (candidateTokens.Any(token => SearchNoiseTerms.Contains(token))) continue;
                var candidate = string.Join(' ', candidateTokens);
                if (OfficialBrandDomains.ContainsKey(NormalizeCompact(candidate))) return candidate;
            }
        }
        return tokens.FirstOrDefault(token => token.Any(char.IsLetter) && !SearchNoiseTerms.Contains(token));
    }

    private static string? DetectBrandFromHost(string host)
    {
        var match = OfficialBrandDomains.FirstOrDefault(pair =>
            pair.Value.Any(domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                                     host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase)));
        return string.IsNullOrWhiteSpace(match.Key)
            ? null
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(match.Key);
    }

    private static bool LooksLikeScreenSize(string token)
    {
        var compact = NormalizeCompact(token);
        if (!compact.Any(char.IsDigit)) return false;
        return compact.EndsWith("inch", StringComparison.OrdinalIgnoreCase) ||
               compact.EndsWith("inches", StringComparison.OrdinalIgnoreCase) ||
               compact.EndsWith("inc", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractTitle(string html)
    {
        var match = TitleRegex().Match(html);
        return match.Success ? CleanHtml(match.Groups["title"].Value).Split('|', '–').FirstOrDefault()?.Trim() : null;
    }

    private static string CleanHtml(string value) =>
        WhitespaceRegex().Replace(WebUtility.HtmlDecode(HtmlTagRegex().Replace(value, " ")), " ").Trim();

    private static void AddSpecification(
        List<ExternalSpecificationDto> target,
        HashSet<string> seen,
        string group,
        string name,
        string value,
        string? unit)
    {
        name = CleanHtml(name).Trim(':', '-', '–');
        value = CleanHtml(value);
        if (name.Length is < 2 or > 180 || value.Length is < 1 or > 1000 || name.Equals(value, StringComparison.OrdinalIgnoreCase)) return;
        if (seen.Add($"{group}|{name}")) target.Add(new ExternalSpecificationDto(group, name, value, unit));
    }

    private static void AddSpecification(List<ExternalSpecificationDto> target, string group, string name, string? value, string? unit)
    {
        if (!string.IsNullOrWhiteSpace(value) && !target.Any(item => item.Group == group && item.Name == name))
            target.Add(new ExternalSpecificationDto(group, name, value, unit));
    }

    private static void AddSpecification(List<ExternalSpecificationDto> target, string group, string name, decimal? value, string unit)
    {
        if (value is not null) AddSpecification(target, group, name, value.Value.ToString(CultureInfo.InvariantCulture), unit);
    }

    private static decimal? ParseRangeMaximum(string raw)
    {
        var values = NumberRegex().Matches(raw)
            .Select(match => decimal.TryParse(match.Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? (decimal?)value : null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        return values.Length == 0 ? null : values.Max();
    }

    private static decimal ToCentimeters(decimal value, string unit) => NormalizeCompact(unit) switch
    {
        "mm" => value / 10m,
        "m" => value * 100m,
        "in" or "inch" or "inches" => value * 2.54m,
        _ => value
    };

    private static decimal ToKilograms(decimal value, string unit) => NormalizeCompact(unit) switch
    {
        "g" => value / 1000m,
        "lb" or "lbs" => value * 0.45359237m,
        _ => value
    };

    private static bool IsBrandHost(string host, string? brand)
    {
        var normalizedBrand = NormalizeCompact(brand);
        if (normalizedBrand.Length < 2) return false;
        if (OfficialBrandDomains.TryGetValue(normalizedBrand, out var domains))
            return domains.Any(domain => HostMatchesDomain(host, domain));
        return host.Split('.').Any(label => NormalizeCompact(label).Equals(normalizedBrand, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HostMatchesDomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCompact(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static async Task<string?> ReadLimitedStringAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var buffer = new char[8192];
        var result = new System.Text.StringBuilder();
        while (result.Length <= limit)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - result.Length)), cancellationToken);
            if (read == 0) return result.ToString();
            result.Append(buffer, 0, read);
        }
        return null;
    }

    private static async Task<byte[]?> ReadLimitedBytesAsync(
        HttpContent content,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new MemoryStream(Math.Min(limit, 1_048_576));
        var buffer = new byte[81920];
        while (destination.Length <= limit)
        {
            var remaining = limit + 1 - (int)destination.Length;
            var read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0) return destination.ToArray();
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return null;
    }

    private sealed record ProductIdentity(
        string? Brand,
        string? Model,
        string DisplayName,
        string SearchPhrase,
        IReadOnlyList<string> IdentityTokens,
        bool IsDisplay);
    private sealed record CatalogSearchResult(string Title, Uri Url);
    private sealed record MeasurementSet(decimal? WidthCm, decimal? HeightCm, decimal? DepthCm, decimal? WeightKg);

    [GeneratedRegex("<a\\b[^>]*class=[\\\"'][^\\\"']*\\bresult__a\\b[^\\\"']*[\\\"'][^>]*href=[\\\"'](?<url>[^\\\"']+)[\\\"'][^>]*>(?<title>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex GenericSearchResultRegex();

    [GeneratedRegex("<a\\b[^>]*href=[\\\"'](?<url>[^\\\"']+)[\\\"'][^>]*>(?<title>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex CatalogAnchorRegex();

    [GeneratedRegex("<a\\s+href=[\\\"'](?<url>(?:https?:)?//[^\\\"']+)[\\\"'][^>]*class=[\\\"'][^\\\"']*\\b(?:l1|result__a)\\b[^\\\"']*[\\\"']", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex SearchResultLinkRegex();

    [GeneratedRegex("<a\\s+[^>]*class=[\\\"'][^\\\"']*\\b(?:l1|result__a)\\b[^\\\"']*[\\\"'][^>]*href=[\\\"'](?<url>(?:https?:)?//[^\\\"']+)[\\\"']", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex SearchResultLinkClassFirstRegex();

    [GeneratedRegex("<li\\b[^>]*class=[\\\"'][^\\\"']*\\bb_algo\\b[^\\\"']*[\\\"'][^>]*>.*?<h2\\b[^>]*>\\s*<a\\b[^>]*href=[\\\"'](?<url>https?://[^\\\"']+)[\\\"']", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex BingResultLinkRegex();

    [GeneratedRegex("<a\\b[^>]*href=[\\\"'](?<url>(?:(?:https?:)?//|/url\\?)[^\\\"']+)[\\\"']", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex AllAnchorLinkRegex();

    [GeneratedRegex("<loc>\\s*(?<url>https://[^<\\s]+)\\s*</loc>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex SitemapLocationRegex();

    [GeneratedRegex("product-specification__label[^>]*>.*?(?<name><span[^>]*>.*?</span>).*?product-specification__value[^>]*>.*?(?<value><span[^>]*>.*?</span>)", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex LabelValueBlockRegex();

    [GeneratedRegex("<tr\\b[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex TableRowRegex();

    [GeneratedRegex("<(?:th|td)\\b[^>]*>(?<cell>.*?)</(?:th|td)>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 500)]
    private static partial Regex TableCellRegex();

    [GeneratedRegex("<dt\\b[^>]*>(?<name>.*?)</dt>\\s*<dd\\b[^>]*>(?<value>.*?)</dd>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex DefinitionPairRegex();

    [GeneratedRegex("<[^>]+class=[\\\"'][^\\\"']*measurement-name[^\\\"']*[\\\"'][^>]*>(?<name>.*?)</[^>]+>\\s*<[^>]+class=[\\\"'][^\\\"']*measurement-value[^\\\"']*[\\\"'][^>]*>(?<value>.*?)</[^>]+>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex MeasurementSpanPairRegex();

    [GeneratedRegex("[\\\"']name[\\\"']\\s*:\\s*[\\\"'](?<name>Width|Height|Depth|Length)[\\\"'][^{}]{0,180}?[\\\"'](?:measure|text|value)[\\\"']\\s*:\\s*[\\\"'](?<value>\\d+(?:[.,]\\d+)?[^\\\"']{0,60})[\\\"']", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex JsonMeasurementNameFirstRegex();

    [GeneratedRegex("[\\\"'](?:measure|text|value)[\\\"']\\s*:\\s*[\\\"'](?<value>\\d+(?:[.,]\\d+)?[^\\\"']{0,60})[\\\"'][^{}]{0,180}?[\\\"']name[\\\"']\\s*:\\s*[\\\"'](?<name>Width|Height|Depth|Length)[\\\"']", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex JsonMeasurementValueFirstRegex();

    [GeneratedRegex("[\\\"']?\\b(?:name|facet|label|title|key|attribute|specName|specLabel)\\b[\\\"']?\\s*:\\s*[\\\"'](?<name>(?:[^\\\"'\\\\]{0,40}?\\b)?(?:width|height|depth|length|dimensions?)(?:\\b[^\\\"'\\\\]{0,40})?)[\\\"']\\s*,\\s*[\\\"']?\\b(?:value|measure|text|specValue|displayValue|values)\\b[\\\"']?\\s*:\\s*\\[?\\s*[\\\"'](?<value>[^\\\"'\\\\]{0,40}?\\d+(?:[.,]\\d+)?\\s*(?:mm|cm|in|inch|inches)(?![a-z])[^\\\"'\\\\]{0,70})[\\\"']", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ScriptLabelValueRegex();

    [GeneratedRegex("(?<number>\\d+(?:[.,]\\d+)?)\\s*(?<unit>mm|cm)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex MetricMeasurementRegex();

    [GeneratedRegex("<meta[^>]+property=[\\\"']og:title[\\\"'][^>]+content=[\\\"'](?<title>[^\\\"']{3,250})[\\\"']", RegexOptions.IgnoreCase, 500)]
    private static partial Regex OpenGraphTitleRegex();

    [GeneratedRegex("<meta[^>]+name=[\\\"']description[\\\"'][^>]+content=[\\\"'](?<text>[^\\\"']{3,400})[\\\"']", RegexOptions.IgnoreCase, 500)]
    private static partial Regex MetaDescriptionRegex();

    [GeneratedRegex("\\b(monitor|display|screen)s?\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex DisplayPageRegex();

    [GeneratedRegex("\\\\u(?<hex>[0-9a-fA-F]{4})", RegexOptions.None, 1000)]
    private static partial Regex JsonUnicodeEscapeRegex();

    // The look-behind keeps "Cable length: 1.8 m" and similar qualified names out.
    // Separators: "Width: 63.9 mm", "Length|||120 mm", or a label line followed by its value line ("Width\n160 cm").
    [GeneratedRegex("(?<![\\p{L}][ \\t]?)\\b(?<name>(?:(?:min|max)(?:imum|\\.)?[ \\t]+)?(?:Length|Width|Height|Depth))[ \\t]*(?:\\|\\|\\||:|\\r?\\n)\\s*(?<value>\\d+(?:[.,]\\d+)?\\s*(?:mm|cm)\\b(?:(?!\\b(?:Length|Width|Height|Depth|Weight)\\b)[^\\n<\"|]){0,70})", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex TextAxisMeasurementRegex();

    [GeneratedRegex("\\d\\s*(?:in|inch|inches|\\\"|ft|lb|lbs)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex ImperialMeasurementRegex();

    [GeneratedRegex("[\\\"']name[\\\"']\\s*:\\s*[\\\"'](?<name>[^\\\"']{1,100})[\\\"']\\s*,\\s*[\\\"']value[\\\"']\\s*:\\s*(?:[\\\"'](?<value>[^\\\"']{1,100})[\\\"']|(?<value>-?\\d+(?:[.,]\\d+)?))\\s*,\\s*[\\\"']unitText[\\\"']\\s*:\\s*[\\\"'](?<unit>[^\\\"']{1,30})[\\\"']", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex JsonPropertyValueRegex();

    [GeneratedRegex("(?<a>\\d+(?:[.,]\\d+)?(?:\\s*[~-]\\s*\\d+(?:[.,]\\d+)?)?)\\s*(?:\\([WHD]\\))?\\s*[x×]\\s*(?<b>\\d+(?:[.,]\\d+)?(?:\\s*[~-]\\s*\\d+(?:[.,]\\d+)?)?)\\s*(?:\\([WHD]\\))?\\s*[x×]\\s*(?<c>\\d+(?:[.,]\\d+)?(?:\\s*[~-]\\s*\\d+(?:[.,]\\d+)?)?)\\s*(?:\\([WHD]\\))?\\s*(?<unit>mm|cm|m|inches|inch|in|\\\")?", RegexOptions.IgnoreCase, 500)]
    private static partial Regex DimensionTripleRegex();

    [GeneratedRegex("(?<label>(?:product\\s+)?dimensions?[^:\\d]{0,70}?)[:\\s]+(?<value>\\d+(?:[.,]\\d+)?(?:\\s*[~-]\\s*\\d+(?:[.,]\\d+)?)?\\s*[x×]\\s*\\d+(?:[.,]\\d+)?(?:\\s*[~-]\\s*\\d+(?:[.,]\\d+)?)?\\s*[x×]\\s*\\d+(?:[.,]\\d+)?(?:\\s*[~-]\\s*\\d+(?:[.,]\\d+)?)?\\s*(?:mm|cm|m|inches|inch|in|\\\"))", RegexOptions.IgnoreCase, 500)]
    private static partial Regex GenericDimensionRegex();

    [GeneratedRegex("(?<name>(?:product\\s+)?dimensions?\\s*(?:(?:with|without|incl\\.?|excl\\.?)\\s+stand)?\\s*(?:\\([^)]{0,60}\\))?)\\s*[:=-]?\\s*(?<value>\\d+(?:[.,]\\d+)?\\s*[x×]\\s*\\d+(?:[.,]\\d+)?\\s*[x×]\\s*\\d+(?:[.,]\\d+)?\\s*(?:mm|cm|m|inches|inch|in|\\\"))", RegexOptions.IgnoreCase, 750)]
    private static partial Regex TextDimensionSpecificationRegex();

    [GeneratedRegex("(?<name>(?:product\\s+)?weight(?:\\s*\\([^)]{0,50}\\)|\\s+(?:net|with stand|without stand))?)\\s*[:=-]?\\s*(?<value>\\d+(?:[.,]\\d+)?\\s*(?:kg|g|lb|lbs))", RegexOptions.IgnoreCase, 500)]
    private static partial Regex TextWeightSpecificationRegex();

    [GeneratedRegex("(?<name>panel size|panel type|panel resolution|resolution|refresh rate|response time|vesa mounting|aspect ratio|brightness|contrast ratio)\\s*[:=-]?\\s*(?<value>[^\\r\\n]{1,120})", RegexOptions.IgnoreCase, 500)]
    private static partial Regex TextFeatureSpecificationRegex();

    [GeneratedRegex("[\\p{L}\\p{Nd}]+(?:[-_][\\p{L}\\p{Nd}]+)*", RegexOptions.None, 500)]
    private static partial Regex QueryTokenRegex();

    // The look-behind stops words such as "Min." or "Main" from reading as inches.
    [GeneratedRegex("(?<![a-z])(?<unit>mm|cm|kg|g|lb|lbs|inches|inch|in)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex UnitInLabelRegex();

    [GeneratedRegex("package|packaging|shipping|boxed|box dimensions|gross weight", RegexOptions.IgnoreCase, 250)]
    private static partial Regex PackagingTermsRegex();

    [GeneratedRegex("\\d+(?:[.,]\\d+)?", RegexOptions.None, 250)]
    private static partial Regex NumberRegex();

    [GeneratedRegex("<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 500)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline, 1000)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.None, 500)]
    private static partial Regex WhitespaceRegex();
}
