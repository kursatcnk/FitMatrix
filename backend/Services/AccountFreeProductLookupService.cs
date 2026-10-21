using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitMatrix.Api.DTOs;

namespace FitMatrix.Api.Services;

public sealed partial class AccountFreeProductLookupService(
    HttpClient httpClient,
    ILogger<AccountFreeProductLookupService> logger)
{
    public async Task<ExternalProductCandidateDto?> FindAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var item = (await SearchItemsAsync(query, cancellationToken)).FirstOrDefault();
        if (item is null) return null;

        var dimensions = ParseProviderDimensions(item.Dimension);
        var officialMeasurements = await TryReadOfficialMeasurementsAsync(item, cancellationToken);
        var widthCm = officialMeasurements?.WidthCm ?? dimensions?.WidthCm;
        var heightCm = officialMeasurements?.HeightCm ?? dimensions?.HeightCm;
        var depthCm = officialMeasurements?.DepthCm ?? dimensions?.DepthCm;
        var weightKg = officialMeasurements?.WeightKg ?? ParseMeasurement(item.Weight, "kg");
        var sourceUrl = officialMeasurements?.SourceUrl
            ?? item.SourceUrl
            ?? $"https://www.upcitemdb.com/upc/{Uri.EscapeDataString(item.Ean ?? item.Upc ?? string.Empty)}";

        var specifications = new List<ExternalSpecificationDto>();
        AddSpecification(specifications, "Kimlik", "GTIN / EAN", item.Ean, null);
        AddSpecification(specifications, "Kimlik", "UPC", item.Upc, null);
        AddSpecification(specifications, "Kimlik", "Sağlayıcı model alanı", item.Model, null);
        AddSpecification(specifications, "Ürün", "Kategori", item.Category, null);
        AddSpecification(specifications, "Ürün", "Boyut (ham kaynak)", item.Dimension, null);
        AddSpecification(specifications, "Ürün", "Ağırlık (ham kaynak)", item.Weight, null);
        AddSpecification(specifications, "Ürün", "Boyut / varyant", item.Size, null);
        AddSpecification(specifications, "Ürün", "Renk", item.Color, null);
        if (officialMeasurements is not null)
            specifications.AddRange(officialMeasurements.Specifications);

        var hasOfficialDimensions = officialMeasurements is { WidthCm: not null, HeightCm: not null, DepthCm: not null };
        var measurementStatus = hasOfficialDimensions
            ? "official-structured"
            : dimensions is not null
                ? "provider-order"
                : "identity-only";
        var measurementNote = hasOfficialDimensions
            ? "Ölçüler üreticinin resmî ürün sayfasındaki alanlardan çıkarıldı."
            : dimensions is not null
                ? "Sağlayıcının tek satırlık boyut alanı W × D × H olarak yorumlandı; kataloğa almadan önce doğrulanmalı."
                : "Ürün kimliği bulundu; bu kayıtta güvenilir gövde ölçüsü yayımlanmamış.";

        var requestedModel = ResolveRequestedModel(query, $"{item.Title} {officialMeasurements?.SourceUrl}") ?? item.Model;
        return new ExternalProductCandidateDto(
            "Hesapsız katalog + resmî kaynak",
            item.Title,
            item.Brand,
            requestedModel,
            requestedModel,
            item.Ean ?? item.Upc,
            widthCm,
            heightCm,
            depthCm,
            weightKg,
            sourceUrl,
            measurementStatus,
            measurementNote,
            specifications);
    }

    public async Task<IReadOnlyList<ExternalProductCandidateDto>> FindManyAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var items = await SearchItemsAsync(query, cancellationToken);
        return items
            .Take(Math.Clamp(limit, 1, 20))
            .Select(BuildProviderCandidate)
            .ToList();
    }

    private async Task<IReadOnlyList<UpcItem>> SearchItemsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var providerQuery = TranslateSearchQuery(query.Trim());
        try
        {
            var requestPath = $"prod/trial/search?s={Uri.EscapeDataString(providerQuery)}&type=product";
            using var response = await httpClient.GetAsync(requestPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (document.RootElement.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                {
                    var primary = itemsElement
                        .EnumerateArray()
                        .Select(ParseItem)
                        .Where(item => item is not null)
                        .Cast<UpcItem>()
                        .OrderByDescending(item => Score(item, providerQuery))
                        .ToList();
                    if (primary.Count > 0) return primary;
                }
            }
            else if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound))
            {
                response.EnsureSuccessStatusCode();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogInformation(exception, "Primary account-free product catalog was unavailable for {Query}", query);
        }

        return await SearchUpcDevAsync(providerQuery, cancellationToken);
    }

    private async Task<IReadOnlyList<UpcItem>> SearchUpcDevAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var items = new List<UpcItem>();
        foreach (var variant in BuildCatalogQueryVariants(query))
            items.AddRange(await ReadUpcDevResultsAsync(variant, cancellationToken));

        return items
            .Where(item => ProductMatchesQuery(item, query))
            .GroupBy(item => item.Ean ?? item.Upc ?? Normalize(item.Title), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => Score(item, query))
            .ToList();
    }

    private async Task<IReadOnlyList<UpcItem>> ReadUpcDevResultsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://upc.dev/v1/search?q={Uri.EscapeDataString(query)}&limit=30");
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound)
            return [];
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];
        return results.EnumerateArray().Select(ParseUpcDevItem).Where(item => item is not null).Cast<UpcItem>().ToList();
    }

    private static IReadOnlyList<string> BuildCatalogQueryVariants(string query)
    {
        var normalized = Normalize(query);
        if (normalized == "keyboard") return ["Razer", "Logitech", "keyboard"];
        if (normalized == "mouse") return ["Razer", "Logitech", "mouse"];
        if (normalized == "monitor") return ["MSI", "AOC", "Dell", "monitor"];
        if (normalized == "headset") return ["Razer", "Logitech", "headset"];
        if (normalized == "pc case") return ["Corsair", "Cooler Master", "pc case"];
        if (normalized == "desk") return ["IKEA", "Flexispot", "desk"];
        if (normalized == "chair") return ["Herman Miller", "Secretlab", "chair"];
        return [query];
    }

    private static bool ProductMatchesQuery(UpcItem item, string query)
    {
        if (BundleTermsRegex().IsMatch(item.Title) || AccessoryTermsRegex().IsMatch(item.Title)) return false;
        var normalized = Normalize(query);
        var title = Normalize(item.Title);
        return normalized switch
        {
            "keyboard" or "mouse" or "monitor" => ProductGuruLookupService.MatchesEquipmentCategory(item.Title, normalized),
            "headset" => title.Contains("headset", StringComparison.Ordinal) || title.Contains("headphone", StringComparison.Ordinal),
            "pc case" => title.Contains("case", StringComparison.Ordinal) || title.Contains("tower", StringComparison.Ordinal),
            "desk" => title.Contains("desk", StringComparison.Ordinal),
            "chair" => title.Contains("chair", StringComparison.Ordinal),
            _ => true
        };
    }

    private static string TranslateSearchQuery(string query)
    {
        var translated = query;
        foreach (var (source, target) in new (string Source, string Target)[]
        {
            ("bilgisayar kasası", "pc case"), ("monitör kolu", "monitor arm"),
            ("klavye", "keyboard"), ("fare", "mouse"), ("monitör", "monitor"), ("ekran", "monitor"),
            ("kasa", "pc case"), ("masa", "desk"), ("sandalye", "chair"), ("kulaklık", "headset"),
            ("hoparlör", "speaker"), ("yazıcı", "printer"), ("kamera", "webcam"), ("telefon", "smartphone")
        })
            translated = Regex.Replace(translated, $"(?<![\\p{{L}}\\p{{Nd}}]){Regex.Escape(source)}(?![\\p{{L}}\\p{{Nd}}])", target, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        return translated;
    }

    private static ExternalProductCandidateDto BuildProviderCandidate(UpcItem item)
    {
        var dimensions = ParseProviderDimensions(item.Dimension);
        var specifications = new List<ExternalSpecificationDto>();
        AddSpecification(specifications, "Kimlik", "GTIN / EAN", item.Ean, null);
        AddSpecification(specifications, "Kimlik", "UPC", item.Upc, null);
        AddSpecification(specifications, "Kimlik", "Sağlayıcı model alanı", item.Model, null);
        AddSpecification(specifications, "Ürün", "Kategori", item.Category, null);
        AddSpecification(specifications, "Ürün", "Boyut (ham kaynak)", item.Dimension, null);
        AddSpecification(specifications, "Ürün", "Ağırlık (ham kaynak)", item.Weight, null);
        AddSpecification(specifications, "Ürün", "Boyut / varyant", item.Size, null);
        AddSpecification(specifications, "Ürün", "Renk", item.Color, null);
        var hasDimensions = dimensions is not null;
        var gtin = item.Ean ?? item.Upc;
        return new ExternalProductCandidateDto(
            "Hesapsız barkod kataloğu",
            item.Title,
            item.Brand,
            item.Model,
            item.Model,
            gtin,
            dimensions?.WidthCm,
            dimensions?.HeightCm,
            dimensions?.DepthCm,
            ParseMeasurement(item.Weight, "kg"),
            item.SourceUrl ?? (string.IsNullOrWhiteSpace(gtin) ? null : $"https://www.upcitemdb.com/upc/{Uri.EscapeDataString(gtin)}"),
            hasDimensions ? "provider-order" : "identity-only",
            hasDimensions
                ? "Sağlayıcının W × D × H boyut alanı bulundu; 3D kataloğa almadan önce doğrulanmalı."
                : "Ürün kimliği bulundu; güvenilir gövde ölçüsü için model taraması gerekir.",
            specifications);
    }

    private async Task<OfficialMeasurements?> TryReadOfficialMeasurementsAsync(
        UpcItem item,
        CancellationToken cancellationToken)
    {
        var officialOffer = item.Offers.FirstOrDefault(offer => IsLikelyOfficialDomain(item.Brand, offer.Domain));
        if (officialOffer is null || !Uri.TryCreate(officialOffer.Link, UriKind.Absolute, out var offerUri)) return null;
        if (!offerUri.Host.EndsWith("upcitemdb.com", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            using var response = await httpClient.GetAsync(offerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null || !HostMatches(finalUri.Host, officialOffer.Domain)) return null;

            var html = await ReadLimitedStringAsync(response.Content, 3_000_000, cancellationToken);
            if (html is null) return null;

            var visibleText = ExtractVisibleText(html);
            var grouped = ExtractGroupedMeasurements(visibleText);
            var fromJsonLd = ExtractJsonLdMeasurements(html);
            var width = grouped.WidthCm ?? fromJsonLd.WidthCm ?? ExtractNamedMeasurement(visibleText, "width", "cm");
            var height = grouped.HeightCm ?? fromJsonLd.HeightCm ?? ExtractNamedMeasurement(visibleText, "height", "cm");
            var depth = grouped.DepthCm ?? fromJsonLd.DepthCm ?? ExtractNamedMeasurement(visibleText, "depth", "cm");
            var weight = grouped.WeightKg ?? fromJsonLd.WeightKg ?? ExtractNamedMeasurement(visibleText, "weight", "kg");
            var specs = ExtractStructuredSpecifications(html);
            AddSpecification(specs, "Resmî üretici", "Genişlik", width?.ToString(CultureInfo.InvariantCulture), "cm");
            AddSpecification(specs, "Resmî üretici", "Yükseklik", height?.ToString(CultureInfo.InvariantCulture), "cm");
            AddSpecification(specs, "Resmî üretici", "Derinlik", depth?.ToString(CultureInfo.InvariantCulture), "cm");
            AddSpecification(specs, "Resmî üretici", "Ağırlık", weight?.ToString(CultureInfo.InvariantCulture), "kg");
            if (width is null && height is null && depth is null && weight is null && specs.Count == 0) return null;
            var cleanSourceUri = new UriBuilder(finalUri) { Query = string.Empty, Fragment = string.Empty }.Uri.ToString();
            return new OfficialMeasurements(width, height, depth, weight, cleanSourceUri, specs);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or RegexMatchTimeoutException)
        {
            logger.LogInformation(exception, "Official product page enrichment was unavailable for {Product}", item.Title);
            return null;
        }
    }

    private static MeasurementSet ExtractJsonLdMeasurements(string html)
    {
        foreach (Match match in JsonLdScriptRegex().Matches(html))
        {
            try
            {
                var json = WebUtility.HtmlDecode(match.Groups["json"].Value);
                using var document = JsonDocument.Parse(json);
                foreach (var product in FindProductObjects(document.RootElement))
                {
                    var result = new MeasurementSet(
                        ReadJsonMeasurement(product, "width", "cm"),
                        ReadJsonMeasurement(product, "height", "cm"),
                        ReadJsonMeasurement(product, "depth", "cm"),
                        ReadJsonMeasurement(product, "weight", "kg"));
                    if (result.WidthCm is not null || result.HeightCm is not null || result.DepthCm is not null || result.WeightKg is not null)
                        return result;
                }
            }
            catch (JsonException)
            {
                // Many storefronts embed more than one JSON value in a script tag; skip malformed blocks.
            }
        }
        return new MeasurementSet(null, null, null, null);
    }

    private static List<ExternalSpecificationDto> ExtractStructuredSpecifications(string html)
    {
        var specifications = new List<ExternalSpecificationDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in JsonLdScriptRegex().Matches(html))
        {
            try
            {
                var json = WebUtility.HtmlDecode(match.Groups["json"].Value);
                using var document = JsonDocument.Parse(json);
                foreach (var product in FindProductObjects(document.RootElement))
                {
                    foreach (var (propertyName, displayName) in new[]
                    {
                        ("sku", "SKU"), ("mpn", "MPN"), ("gtin", "GTIN"), ("gtin8", "GTIN-8"),
                        ("gtin12", "GTIN-12"), ("gtin13", "GTIN-13"), ("gtin14", "GTIN-14"),
                        ("model", "Model"), ("color", "Renk"), ("material", "Malzeme"), ("category", "Kategori")
                    })
                    {
                        if (product.TryGetProperty(propertyName, out var value))
                            AddJsonSpecification(specifications, seen, "Ürün kimliği", displayName, value, null);
                    }

                    if (product.TryGetProperty("brand", out var brand))
                    {
                        if (brand.ValueKind == JsonValueKind.Object && brand.TryGetProperty("name", out var brandName))
                            AddJsonSpecification(specifications, seen, "Ürün kimliği", "Marka", brandName, null);
                        else
                            AddJsonSpecification(specifications, seen, "Ürün kimliği", "Marka", brand, null);
                    }

                    if (!product.TryGetProperty("additionalProperty", out var additional)) continue;
                    IEnumerable<JsonElement> properties = additional.ValueKind == JsonValueKind.Array
                        ? additional.EnumerateArray().ToArray()
                        : [additional];
                    foreach (var property in properties)
                    {
                        if (property.ValueKind != JsonValueKind.Object || !property.TryGetProperty("name", out var nameElement)) continue;
                        var name = JsonScalar(nameElement);
                        if (string.IsNullOrWhiteSpace(name) || !property.TryGetProperty("value", out var valueElement)) continue;
                        var unit = property.TryGetProperty("unitText", out var unitElement) ? JsonScalar(unitElement) : null;
                        AddJsonSpecification(specifications, seen, "Teknik özellikler", name, valueElement, unit);
                    }
                }
            }
            catch (JsonException)
            {
                // Skip malformed storefront blocks and continue with HTML tables.
            }
        }

        foreach (Match rowMatch in HtmlTableRowRegex().Matches(html))
        {
            var cells = HtmlTableCellRegex().Matches(rowMatch.Groups["row"].Value)
                .Select(cell => ExtractVisibleText(cell.Groups["cell"].Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(3)
                .ToArray();
            if (cells.Length >= 2) AddFlexibleSpecification(specifications, seen, "Teknik tablo", cells[0], string.Join(" · ", cells.Skip(1)), null);
            if (specifications.Count >= 200) break;
        }

        foreach (Match item in HtmlSpecificationItemRegex().Matches(html))
        {
            AddFlexibleSpecification(
                specifications,
                seen,
                "Üretici teknik özellikleri",
                ExtractVisibleText(item.Groups["name"].Value),
                ExtractVisibleText(item.Groups["value"].Value),
                null);
            if (specifications.Count >= 200) break;
        }

        var decodedHtml = WebUtility.HtmlDecode(html);
        foreach (Match row in AccessibleSpecificationRowRegex().Matches(decodedHtml))
        {
            AddFlexibleSpecification(
                specifications,
                seen,
                "Üretici teknik özellikleri",
                ExtractVisibleText(row.Groups["name"].Value),
                ExtractVisibleText(row.Groups["value"].Value),
                null);
            if (specifications.Count >= 200) break;
        }

        foreach (Match pair in HtmlDefinitionPairRegex().Matches(html))
        {
            AddFlexibleSpecification(
                specifications,
                seen,
                "Teknik ayrıntılar",
                ExtractVisibleText(pair.Groups["name"].Value),
                ExtractVisibleText(pair.Groups["value"].Value),
                null);
            if (specifications.Count >= 200) break;
        }

        return specifications.Take(200).ToList();
    }

    private static void AddJsonSpecification(
        List<ExternalSpecificationDto> target,
        HashSet<string> seen,
        string group,
        string name,
        JsonElement value,
        string? unit)
    {
        var scalar = JsonScalar(value);
        if (scalar is not null) AddFlexibleSpecification(target, seen, group, name, scalar, unit);
    }

    private static string? JsonScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
        JsonValueKind.Object when value.TryGetProperty("name", out var name) => JsonScalar(name),
        _ => null
    };

    private static void AddFlexibleSpecification(
        List<ExternalSpecificationDto> target,
        HashSet<string> seen,
        string group,
        string name,
        string value,
        string? unit)
    {
        name = WhitespaceRegex().Replace(WebUtility.HtmlDecode(name), " ").Trim().Trim(':', '-', '–');
        value = WhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ").Trim();
        if (name.Length is < 2 or > 180 || value.Length is < 1 or > 1000) return;
        if (name.Equals(value, StringComparison.OrdinalIgnoreCase) || PackagingTermsRegex().IsMatch(name)) return;
        var key = $"{group}|{name}";
        if (seen.Add(key)) target.Add(new ExternalSpecificationDto(group, name, value, unit));
    }

    private static IEnumerable<JsonElement> FindProductObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("@type", out var type) &&
                ((type.ValueKind == JsonValueKind.String && type.GetString()?.Equals("Product", StringComparison.OrdinalIgnoreCase) == true) ||
                 (type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(value => value.GetString()?.Equals("Product", StringComparison.OrdinalIgnoreCase) == true))))
                yield return element;

            foreach (var property in element.EnumerateObject())
            foreach (var match in FindProductObjects(property.Value))
                yield return match;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            foreach (var match in FindProductObjects(child))
                yield return match;
        }
    }

    private static decimal? ReadJsonMeasurement(JsonElement product, string propertyName, string targetUnit)
    {
        if (!product.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String) return ParseMeasurement(value.GetString(), targetUnit);
        if (value.ValueKind != JsonValueKind.Object) return null;

        string? rawValue = null;
        if (value.TryGetProperty("value", out var nestedValue)) rawValue = nestedValue.ToString();
        var unit = value.TryGetProperty("unitText", out var unitText)
            ? unitText.GetString()
            : value.TryGetProperty("unitCode", out var unitCode) ? unitCode.GetString() : null;
        return ParseMeasurement($"{rawValue} {unit}", targetUnit);
    }

    private static decimal? ExtractNamedMeasurement(string html, string name, string targetUnit)
    {
        var pattern = $"(?is)\\b(?:product\\s*)?{Regex.Escape(name)}(?:\\s*\\(with\\s+stand\\))?\\s*[:=-]\\s*(?<value>\\d{{1,4}}(?:[.,]\\d{{1,3}})?)\\s*(?<unit>mm|cm|m|kg|g|lb|lbs|inches|inch|in\\b|\\\")";
        foreach (Match match in Regex.Matches(html, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)))
        {
            var start = Math.Max(0, match.Index - 100);
            var context = html.Substring(start, Math.Min(match.Length + 200, html.Length - start));
            if (PackagingTermsRegex().IsMatch(context)) continue;
            return ParseMeasurement($"{match.Groups["value"].Value} {match.Groups["unit"].Value}", targetUnit);
        }
        return null;
    }

    private static MeasurementSet ExtractGroupedMeasurements(string text)
    {
        var detailed = WithStandDetailsRegex().Match(text);
        if (detailed.Success)
        {
            return new MeasurementSet(
                ParseMeasurement($"{detailed.Groups["width"].Value} {detailed.Groups["widthUnit"].Value}", "cm"),
                ParseMeasurement($"{detailed.Groups["height"].Value} {detailed.Groups["heightUnit"].Value}", "cm"),
                ParseMeasurement($"{detailed.Groups["depth"].Value} {detailed.Groups["depthUnit"].Value}", "cm"),
                ParseMeasurement($"{detailed.Groups["weight"].Value} {detailed.Groups["weightUnit"].Value}", "kg"));
        }

        var dimensions = OrderedDimensionsRegex().Match(text);
        if (!dimensions.Success) return new MeasurementSet(null, null, null, null);

        var a = ParseMeasurement($"{dimensions.Groups["a"].Value} {dimensions.Groups["aUnit"].Value}", "cm");
        var b = ParseMeasurement($"{dimensions.Groups["b"].Value} {dimensions.Groups["bUnit"].Value}", "cm");
        var c = ParseMeasurement($"{dimensions.Groups["c"].Value} {dimensions.Groups["cUnit"].Value}", "cm");
        var order = dimensions.Groups["order"].Value.Replace(" ", string.Empty).ToLowerInvariant();
        return order switch
        {
            "wxdxh" => new MeasurementSet(a, c, b, null),
            "wxhxd" => new MeasurementSet(a, b, c, null),
            "lxwxh" => new MeasurementSet(b, c, a, null),
            _ => new MeasurementSet(null, null, null, null)
        };
    }

    private static string ExtractVisibleText(string html)
    {
        var withoutScripts = ScriptAndStyleRegex().Replace(html, " ");
        var withoutTags = HtmlTagRegex().Replace(withoutScripts, " ");
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    private static DimensionSet? ParseProviderDimensions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var match = DimensionRegex().Match(raw);
        if (!match.Success) return null;

        var first = ParseDecimal(match.Groups["first"].Value);
        var second = ParseDecimal(match.Groups["second"].Value);
        var third = ParseDecimal(match.Groups["third"].Value);
        var unit = match.Groups["unit"].Value;
        if (first is null || second is null || third is null) return null;

        // UPCitemdb's field is commonly W x D x H. Keep the raw source in Specifications and mark this as provider-order.
        return new DimensionSet(ToCentimeters(first.Value, unit), ToCentimeters(third.Value, unit), ToCentimeters(second.Value, unit));
    }

    private static decimal? ParseMeasurement(string? raw, string targetUnit)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var match = NumberWithUnitRegex().Match(raw.Trim());
        if (!match.Success) return null;
        var value = ParseDecimal(match.Groups["value"].Value);
        if (value is null) return null;
        var unit = match.Groups["unit"].Value;
        var normalized = targetUnit == "kg" ? ToKilograms(value.Value, unit) : ToCentimeters(value.Value, unit);
        var plausible = targetUnit == "kg"
            ? normalized is > 0.001m and <= 5000m
            : normalized is > 0.05m and <= 500m;
        return plausible ? normalized : null;
    }

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static decimal ToCentimeters(decimal value, string unit) => unit.Trim().ToLowerInvariant() switch
    {
        "mm" => value / 10m,
        "m" => value * 100m,
        "in" or "inch" or "inches" or "\"" => value * 2.54m,
        _ => value
    };

    private static decimal ToKilograms(decimal value, string unit) => unit.Trim().ToLowerInvariant() switch
    {
        "g" => value / 1000m,
        "lb" or "lbs" => value * 0.45359237m,
        _ => value
    };

    private static UpcItem? ParseItem(JsonElement item)
    {
        var title = GetString(item, "title");
        if (string.IsNullOrWhiteSpace(title)) return null;
        var offers = new List<UpcOffer>();
        if (item.TryGetProperty("offers", out var offersElement) && offersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var offer in offersElement.EnumerateArray())
            {
                var domain = GetString(offer, "domain");
                var link = GetString(offer, "link");
                if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(link))
                    offers.Add(new UpcOffer(domain, link));
            }
        }
        return new UpcItem(
            title,
            GetString(item, "brand"),
            GetString(item, "model"),
            GetString(item, "ean"),
            GetString(item, "upc"),
            GetString(item, "dimension"),
            GetString(item, "weight"),
            GetString(item, "size"),
            GetString(item, "color"),
            GetString(item, "category"),
            offers,
            null);
    }

    private static UpcItem? ParseUpcDevItem(JsonElement item)
    {
        var title = GetString(item, "name");
        if (string.IsNullOrWhiteSpace(title)) return null;
        var gtin = GetString(item, "gtin") ?? GetString(item, "upc");
        var relativeUrl = GetString(item, "url");
        var sourceUrl = relativeUrl is not null && Uri.TryCreate(new Uri("https://upc.dev"), relativeUrl, out var absolute)
            ? absolute.ToString()
            : string.IsNullOrWhiteSpace(gtin) ? null : $"https://upc.dev/product/{Uri.EscapeDataString(gtin)}";
        return new UpcItem(
            title,
            GetString(item, "brand"),
            null,
            gtin,
            GetString(item, "upc"),
            null,
            null,
            null,
            null,
            GetString(item, "category"),
            [],
            sourceUrl);
    }

    private static string? GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static int Score(UpcItem item, string query)
    {
        var normalizedQuery = Normalize(query);
        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var modelToken = queryTokens.LastOrDefault(token => token.Any(char.IsDigit));
        var normalizedTitle = Normalize(item.Title);
        var score = 0;
        if (Normalize(item.Ean) == normalizedQuery || Normalize(item.Upc) == normalizedQuery) score += 200;
        if (Normalize(item.Model) == normalizedQuery || (!string.IsNullOrWhiteSpace(item.Model) && normalizedQuery.Contains(Normalize(item.Model)))) score += 100;
        if (normalizedTitle.Contains(normalizedQuery)) score += 80;
        if (modelToken is not null && normalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(modelToken)) score += 80;
        foreach (var token in queryTokens)
            if (normalizedTitle.Contains(token)) score += 8;
        if (item.Offers.Any(offer => IsLikelyOfficialDomain(item.Brand, offer.Domain))) score += 60;
        if (!string.IsNullOrWhiteSpace(item.Dimension)) score += 20;
        if (!string.IsNullOrWhiteSpace(item.Weight)) score += 5;
        if (BundleTermsRegex().IsMatch(item.Title)) score -= 300;
        return score;
    }

    private static bool IsLikelyOfficialDomain(string? brand, string domain)
    {
        var normalizedBrand = NormalizeCompact(brand);
        var normalizedDomain = NormalizeCompact(domain.Split(':')[0].Split('/')[0]);
        return normalizedBrand.Length >= 2 && normalizedDomain.Contains(normalizedBrand, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HostMatches(string host, string declaredDomain)
    {
        var normalizedHost = host.Trim().TrimEnd('.').ToLowerInvariant();
        var normalizedDomain = declaredDomain.Trim().TrimEnd('.').ToLowerInvariant();
        return normalizedHost == normalizedDomain || normalizedHost.EndsWith($".{normalizedDomain}", StringComparison.Ordinal);
    }

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeCompact(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? ResolveRequestedModel(string query, string title)
    {
        var candidate = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(token => token.Any(char.IsDigit));
        return candidate is not null && title.Contains(candidate, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }

    private static async Task<string?> ReadLimitedStringAsync(HttpContent content, int maxCharacters, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var buffer = new char[8192];
        var result = new System.Text.StringBuilder();
        while (result.Length <= maxCharacters)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxCharacters + 1 - result.Length)), cancellationToken);
            if (read == 0) return result.ToString();
            result.Append(buffer, 0, read);
        }
        return null;
    }

    private static void AddSpecification(List<ExternalSpecificationDto> target, string group, string name, string? value, string? unit)
    {
        if (!string.IsNullOrWhiteSpace(value)) target.Add(new ExternalSpecificationDto(group, name, value, unit));
    }

    private sealed record UpcItem(
        string Title,
        string? Brand,
        string? Model,
        string? Ean,
        string? Upc,
        string? Dimension,
        string? Weight,
        string? Size,
        string? Color,
        string? Category,
        IReadOnlyList<UpcOffer> Offers,
        string? SourceUrl);

    private sealed record UpcOffer(string Domain, string Link);
    private sealed record DimensionSet(decimal WidthCm, decimal HeightCm, decimal DepthCm);
    private sealed record MeasurementSet(decimal? WidthCm, decimal? HeightCm, decimal? DepthCm, decimal? WeightKg);
    private sealed record OfficialMeasurements(
        decimal? WidthCm,
        decimal? HeightCm,
        decimal? DepthCm,
        decimal? WeightKg,
        string SourceUrl,
        IReadOnlyList<ExternalSpecificationDto> Specifications);

    [GeneratedRegex("<script[^>]*type=[\\\"']application/ld\\+json[\\\"'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 500)]
    private static partial Regex JsonLdScriptRegex();

    [GeneratedRegex("(?<first>\\d+(?:[.,]\\d+)?)\\s*[x×]\\s*(?<second>\\d+(?:[.,]\\d+)?)\\s*[x×]\\s*(?<third>\\d+(?:[.,]\\d+)?)\\s*(?<unit>mm|cm|m|inches|inch|in|\\\")", RegexOptions.IgnoreCase, 250)]
    private static partial Regex DimensionRegex();

    [GeneratedRegex("(?<value>\\d+(?:[.,]\\d+)?)\\s*(?<unit>mm|cm|m|kg|g|lb|lbs|inches|inch|in|\\\")", RegexOptions.IgnoreCase, 250)]
    private static partial Regex NumberWithUnitRegex();

    [GeneratedRegex("package|packaging|shipping|boxed|box dimensions", RegexOptions.IgnoreCase, 250)]
    private static partial Regex PackagingTermsRegex();

    [GeneratedRegex("bundle|restored|refurbished|renewed|replacement", RegexOptions.IgnoreCase, 250)]
    private static partial Regex BundleTermsRegex();

    [GeneratedRegex("cover|sleeve|assembly|brace|drape|skin|protector|adapter|cable|keycap|switches|wrist rest|carrying case", RegexOptions.IgnoreCase, 250)]
    private static partial Regex AccessoryTermsRegex();

    [GeneratedRegex("with\\s+stand\\s*-\\s*width\\s*:\\s*(?<width>\\d+(?:[.,]\\d+)?)\\s*(?<widthUnit>mm|cm|m|inches|inch|in|\\\")\\s*-\\s*depth\\s*:\\s*(?<depth>\\d+(?:[.,]\\d+)?)\\s*(?<depthUnit>mm|cm|m|inches|inch|in|\\\")\\s*-\\s*height\\s*:\\s*(?<height>\\d+(?:[.,]\\d+)?)\\s*(?<heightUnit>mm|cm|m|inches|inch|in|\\\")\\s*-\\s*weight\\s*:\\s*(?<weight>\\d+(?:[.,]\\d+)?)\\s*(?<weightUnit>kg|g|lb|lbs)", RegexOptions.IgnoreCase, 500)]
    private static partial Regex WithStandDetailsRegex();

    [GeneratedRegex("dimensions\\s*\\((?<order>w\\s*x\\s*d\\s*x\\s*h|w\\s*x\\s*h\\s*x\\s*d|l\\s*x\\s*w\\s*x\\s*h)\\)\\s*(?<a>\\d+(?:[.,]\\d+)?)\\s*(?<aUnit>mm|cm|m|inches|inch|in|\\\")\\s*x\\s*(?<b>\\d+(?:[.,]\\d+)?)\\s*(?<bUnit>mm|cm|m|inches|inch|in|\\\")\\s*x\\s*(?<c>\\d+(?:[.,]\\d+)?)\\s*(?<cUnit>mm|cm|m|inches|inch|in|\\\")", RegexOptions.IgnoreCase, 500)]
    private static partial Regex OrderedDimensionsRegex();

    [GeneratedRegex("<(script|style)\\b[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex ScriptAndStyleRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline, 1000)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("<tr\\b[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex HtmlTableRowRegex();

    [GeneratedRegex("<(?:th|td)\\b[^>]*>(?<cell>.*?)</(?:th|td)>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 500)]
    private static partial Regex HtmlTableCellRegex();

    [GeneratedRegex("<dt\\b[^>]*>(?<name>.*?)</dt>\\s*<dd\\b[^>]*>(?<value>.*?)</dd>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex HtmlDefinitionPairRegex();

    [GeneratedRegex("<div\\b[^>]*class=[\\\"'][^\\\"']*spec__item[^\\\"']*[\\\"'][^>]*>\\s*<div\\b[^>]*class=[\\\"'][^\\\"']*spec__item__title[^\\\"']*[\\\"'][^>]*>(?<name>.*?)</div>\\s*(?<value>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex HtmlSpecificationItemRegex();

    [GeneratedRegex("role=[\\\"']rowheader[\\\"'][^>]*>(?<name>.*?)</[^>]+>.*?role=[\\\"']cell[\\\"'][^>]*>(?<value>.*?)</[^>]+>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex AccessibleSpecificationRowRegex();

    [GeneratedRegex("\\s+", RegexOptions.None, 500)]
    private static partial Regex WhitespaceRegex();
}
