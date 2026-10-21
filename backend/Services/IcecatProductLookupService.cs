using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FitMatrix.Api.DTOs;
using Microsoft.Extensions.Options;

namespace FitMatrix.Api.Services;

public sealed class IcecatOptions
{
    public const string SectionName = "Icecat";
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string Language { get; init; } = "EN";
}

public sealed class IcecatProductLookupService(
    HttpClient httpClient,
    IOptions<IcecatOptions> options)
{
    private readonly IcecatOptions _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Username) &&
        !string.IsNullOrWhiteSpace(_options.Password);

    public async Task<ExternalProductCandidateDto?> FindAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured) return null;

        var requestPath = BuildRequestPath(query);
        if (requestPath is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null
        });
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        return Parse(document);
    }

    private string? BuildRequestPath(string query)
    {
        var trimmed = query.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        var language = Uri.EscapeDataString(_options.Language.Trim().ToLowerInvariant());

        if (digits.Length is 8 or 12 or 13 or 14 && digits.Length == trimmed.Count(char.IsDigit))
        {
            return $"xml_s3/xml_server3.cgi?ean_upc={Uri.EscapeDataString(digits)};lang={language};output=productxml";
        }

        string brand;
        string productCode;
        var separator = trimmed.IndexOf('|');
        if (separator > 0 && separator < trimmed.Length - 1)
        {
            brand = trimmed[..separator].Trim();
            productCode = trimmed[(separator + 1)..].Trim();
        }
        else
        {
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            brand = parts[0];
            productCode = parts[^1];
        }
        return $"xml_s3/xml_server3.cgi?prod_id={Uri.EscapeDataString(productCode)};vendor={Uri.EscapeDataString(brand)};lang={language};output=productxml";
    }

    private static ExternalProductCandidateDto? Parse(XDocument document)
    {
        var root = document.Root;
        var product = root?.Descendants("Product").FirstOrDefault();
        if (product is null) return null;

        var specifications = product.Descendants("ProductFeature")
            .Select(feature =>
            {
                var definition = feature.Element("Feature");
                var group = feature.Ancestors("CategoryFeatureGroup").FirstOrDefault()?.Element("FeatureGroup")?.Attribute("Name")?.Value
                    ?? feature.Attribute("CategoryFeatureGroup_ID")?.Value
                    ?? "Genel";
                var name = definition?.Attribute("Name")?.Value ?? feature.Attribute("Feature_ID")?.Value ?? "Özellik";
                var value = feature.Attribute("Presentation_Value")?.Value
                    ?? feature.Attribute("LocalValue")?.Value
                    ?? feature.Attribute("Value")?.Value
                    ?? string.Empty;
                var unit = feature.Element("Measure")?.Attribute("Sign")?.Value;
                return new ExternalSpecificationDto(group, name, value, unit);
            })
            .Where(specification => specification.Value.Length > 0)
            .ToList();

        var title = product.Attribute("Title")?.Value
            ?? product.Attribute("Name")?.Value
            ?? "Icecat ürünü";
        var supplier = product.Element("Supplier")?.Attribute("Name")?.Value;
        var productCode = product.Attribute("Prod_id")?.Value;
        var gtin = product.Descendants("EAN_UPC").Select(node => node.Attribute("Value")?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var productUrl = product.Element("ProductDescription")?.Attribute("URL")?.Value;

        return new ExternalProductCandidateDto(
            "Open Icecat",
            title,
            supplier,
            product.Attribute("Name")?.Value,
            productCode,
            gtin,
            FindMeasurement(specifications, ["width (with stand)", "product width", "width"], "cm"),
            FindMeasurement(specifications, ["height (with stand)", "product height", "height"], "cm"),
            FindMeasurement(specifications, ["depth (with stand)", "product depth", "depth", "length"], "cm"),
            FindMeasurement(specifications, ["weight (with stand)", "product weight", "weight"], "kg"),
            productUrl,
            "source-structured",
            "Ölçüler yapılandırılmış sağlayıcı alanlarından alındı.",
            specifications);
    }

    private static decimal? FindMeasurement(
        IReadOnlyList<ExternalSpecificationDto> specifications,
        IReadOnlyList<string> preferredNames,
        string targetUnit)
    {
        ExternalSpecificationDto? match = null;
        foreach (var preferredName in preferredNames)
        {
            match = specifications.FirstOrDefault(specification =>
            {
                var name = specification.Name.Trim().ToLowerInvariant();
                var group = specification.Group.ToLowerInvariant();
                return name == preferredName &&
                    !name.Contains("package") &&
                    !name.Contains("shipping") &&
                    !group.Contains("packaging");
            });
            if (match is not null) break;
        }
        if (match is null) return null;

        var normalized = match.Value.Replace(',', '.');
        var numeric = new string(normalized.TakeWhile(character => char.IsDigit(character) || character is '.' or '-').ToArray());
        if (!decimal.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return null;

        var unit = (match.Unit ?? match.Value[numeric.Length..]).Trim().ToLowerInvariant();
        return targetUnit == "kg" ? ToKilograms(value, unit) : ToCentimeters(value, unit);
    }

    private static decimal ToCentimeters(decimal value, string unit) => unit switch
    {
        "mm" => value / 10m,
        "m" => value * 100m,
        "in" or "\"" => value * 2.54m,
        _ => value
    };

    private static decimal ToKilograms(decimal value, string unit) => unit switch
    {
        "g" => value / 1000m,
        "lb" or "lbs" => value * 0.45359237m,
        _ => value
    };
}
