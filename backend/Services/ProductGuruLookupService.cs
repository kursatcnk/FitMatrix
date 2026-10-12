using System.Net;
using System.Text.RegularExpressions;
using FitMatrix.Api.DTOs;

namespace FitMatrix.Api.Services;

/// <summary>
/// Searches ProductGuru's public, account-free barcode index. Broad results are
/// intentionally identity-only; the existing deep scanner resolves dimensions
/// from manufacturer and technical sources after the user selects a model.
/// </summary>
public sealed partial class ProductGuruLookupService(
    HttpClient httpClient,
    ILogger<ProductGuruLookupService> logger)
{
    public async Task<IReadOnlyList<ExternalProductCandidateDto>> FindManyAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var translated = TranslateQuery(query);
        var requests = BuildQueryVariants(translated)
            .SelectMany(variant => Enumerable.Range(1, 2).Select(page => (Variant: variant, Page: page)))
            .Take(4)
            .ToArray();
        var discovered = new List<ProductGuruItem>();
        foreach (var request in requests)
        {
            discovered.AddRange(await ReadPageAsync(request.Variant, request.Page, cancellationToken));
            if (discovered.Count >= limit * 2) break;
        }

        return discovered
            .Where(item => MatchesEquipmentCategory(item.Title, translated))
            .GroupBy(item => item.Gtin, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(Math.Clamp(limit, 1, 80))
            .Select(item => new ExternalProductCandidateDto(
                "ProductGuru açık ürün indeksi",
                item.Title,
                item.Brand,
                ExtractModel(item.Title, item.Brand),
                ExtractModel(item.Title, item.Brand),
                item.Gtin,
                null,
                null,
                null,
                null,
                $"https://myproduct.guru/p/{Uri.EscapeDataString(item.Gtin)}",
                "identity-only",
                "Barkodlu gerçek ürün kaydı bulundu. Fiziksel ölçüler, model seçildiğinde üretici ve teknik kaynaklardan ayrıca taranır.",
                [
                    new ExternalSpecificationDto("Kimlik", "GTIN / EAN", item.Gtin, null),
                    new ExternalSpecificationDto("Kimlik", "Marka", item.Brand ?? "—", null),
                    new ExternalSpecificationDto("Kaynak", "Ürün indeksi", "ProductGuru", null)
                ]))
            .ToList();
    }

    private async Task<IReadOnlyList<ProductGuruItem>> ReadPageAsync(
        string query,
        int page,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = $"search?q={Uri.EscapeDataString(query)}&page={page}";
            using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return [];
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (html.Length > 3_000_000) return [];

            return ProductCardRegex().Matches(html)
                .Cast<Match>()
                .Select(match => new ProductGuruItem(
                    CleanHtml(match.Groups["title"].Value),
                    NullIfEmpty(CleanHtml(match.Groups["brand"].Value)),
                    match.Groups["gtin"].Value))
                .Where(item => item.Title.Length is >= 4 and <= 260)
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or RegexMatchTimeoutException)
        {
            logger.LogInformation(exception, "ProductGuru catalog page was unavailable for {Query}, page {Page}", query, page);
            return [];
        }
    }

    private static IReadOnlyList<string> BuildQueryVariants(string query)
    {
        var normalized = Normalize(query);
        if (normalized == "keyboard") return ["computer keyboard", "gaming keyboard", "mechanical keyboard", "wireless keyboard"];
        if (normalized == "mouse") return ["computer mouse", "gaming mouse", "wireless mouse", "ergonomic mouse"];
        if (normalized == "monitor") return ["computer monitor", "gaming monitor", "office monitor"];
        if (normalized == "pc case") return ["computer case", "gaming pc case", "desktop tower case"];
        if (normalized == "headset") return ["gaming headset", "computer headset", "wireless headset"];
        if (normalized == "speaker") return ["computer speaker", "desktop speaker", "pc speakers"];
        if (normalized == "webcam") return ["computer webcam", "usb webcam", "streaming webcam"];
        if (normalized == "desk") return ["computer desk", "gaming desk", "office desk"];
        if (normalized == "chair") return ["office chair", "gaming chair", "computer chair"];
        return [query];
    }

    public static bool MatchesEquipmentCategory(string title, string query)
    {
        var value = Normalize(title);
        var category = Normalize(query);
        if (CommonAccessoryNoiseRegex().IsMatch(value)) return false;

        return category switch
        {
            "keyboard" => KeyboardTermRegex().IsMatch(value) && KeyboardEvidenceRegex().IsMatch(value) && !KeyboardNoiseRegex().IsMatch(value),
            "mouse" => MouseTermRegex().IsMatch(value) && MouseEvidenceRegex().IsMatch(value) && !MouseNoiseRegex().IsMatch(value),
            "monitor" => MonitorTermRegex().IsMatch(value) && !MonitorNoiseRegex().IsMatch(value),
            "pc case" => PcCaseTermRegex().IsMatch(value),
            "headset" => HeadsetTermRegex().IsMatch(value),
            "speaker" => SpeakerTermRegex().IsMatch(value),
            "webcam" => WebcamTermRegex().IsMatch(value),
            "desk" => DeskTermRegex().IsMatch(value),
            "chair" => ChairTermRegex().IsMatch(value),
            _ => value.Contains(category, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string TranslateQuery(string query)
    {
        var translated = Normalize(query);
        foreach (var (source, target) in new (string Source, string Target)[]
        {
            ("bilgisayar kasası", "pc case"), ("klavye", "keyboard"), ("fare", "mouse"),
            ("monitör", "monitor"), ("ekran", "monitor"), ("kasa", "pc case"),
            ("kulaklık", "headset"), ("hoparlör", "speaker"), ("kamera", "webcam"),
            ("masa", "desk"), ("sandalye", "chair")
        })
            translated = Regex.Replace(translated, $"(?<![\\p{{L}}\\p{{Nd}}]){Regex.Escape(source)}(?![\\p{{L}}\\p{{Nd}}])", target, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        return translated;
    }

    private static string? ExtractModel(string title, string? brand)
    {
        var tokens = Regex.Matches(title, "[\\p{L}\\p{Nd}]+(?:[-_.][\\p{L}\\p{Nd}]+)*")
            .Select(match => match.Value)
            .Where(token => !string.Equals(token, brand, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var modelIndex = Array.FindIndex(tokens, token => token.Any(char.IsDigit) && token.Any(char.IsLetter));
        if (modelIndex < 0) modelIndex = Array.FindIndex(tokens, token => token.Any(char.IsDigit));
        return modelIndex < 0 ? null : string.Join(' ', tokens.Skip(modelIndex).Take(3));
    }

    private static string CleanHtml(string value) =>
        WhitespaceRegex().Replace(WebUtility.HtmlDecode(HtmlTagRegex().Replace(value, " ")), " ").Trim();

    private static string Normalize(string value) =>
        WhitespaceRegex().Replace(WebUtility.HtmlDecode(value).Trim().ToLowerInvariant(), " ");

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record ProductGuruItem(string Title, string? Brand, string Gtin);

    [GeneratedRegex("<a[^>]+href=[\\\"']/p/(?<gtin>\\d{8,14})[\\\"'][^>]*class=[\\\"'][^\\\"']*result-card[^\\\"']*[\\\"'][^>]*>.*?<strong[^>]*>(?<title>.*?)</strong>.*?<span[^>]*>(?<brand>.*?)</span>.*?</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex ProductCardRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline, 500)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.None, 500)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("\\b(cover|dust cover|wrist.?rest|cleaner|protector|replacement|sticker|skin|carrying case|adapter|cable|mouse.?pads?|mouse.?mats?)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex CommonAccessoryNoiseRegex();

    [GeneratedRegex("\\b(keyboard|keypad)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex KeyboardTermRegex();

    [GeneratedRegex("\\b(gaming|mechanical|wireless|wired|bluetooth|qwert[yz]?|rgb|computer|pc|mac|usb|keys?|keypad|hot.?swap)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex KeyboardEvidenceRegex();

    [GeneratedRegex("\\b(piano|synth(?:esizer)?|music|book|guide|pedal|stand|keycap|switch(?:es)?|mat)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex KeyboardNoiseRegex();

    [GeneratedRegex("\\bmouse\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex MouseTermRegex();

    [GeneratedRegex("\\b(gaming|wireless|wired|optical|bluetooth|dpi|ergonomic|computer|pc|usb|rechargeable|vertical|trackball|laser)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex MouseEvidenceRegex();

    [GeneratedRegex("\\b(mouse.?(?:pads?|mats?)|mousemats?|jiggler|mover|trap|toy|dvd|movie|book|cheese|sponge|ring light|skates?|feet|grip)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex MouseNoiseRegex();

    [GeneratedRegex("\\b(monitor|display)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex MonitorTermRegex();

    [GeneratedRegex("\\b(stand|arm|mount|cable|screen protector|privacy filter)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex MonitorNoiseRegex();

    [GeneratedRegex("\\b(pc|computer|desktop|gaming).{0,25}\\b(case|tower|chassis)\\b|\\b(case|tower|chassis).{0,25}\\b(pc|computer|desktop|gaming)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex PcCaseTermRegex();

    [GeneratedRegex("\\b(headset|headphones?)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex HeadsetTermRegex();

    [GeneratedRegex("\\b(speakers?|soundbar)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex SpeakerTermRegex();

    [GeneratedRegex("\\b(webcam|web camera)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex WebcamTermRegex();

    [GeneratedRegex("\\b(computer|gaming|office|standing).{0,20}\\bdesk\\b|\\bdesk.{0,20}\\b(computer|gaming|office|standing)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex DeskTermRegex();

    [GeneratedRegex("\\b(office|gaming|computer|ergonomic).{0,20}\\bchair\\b|\\bchair.{0,20}\\b(office|gaming|computer|ergonomic)\\b", RegexOptions.IgnoreCase, 250)]
    private static partial Regex ChairTermRegex();
}
