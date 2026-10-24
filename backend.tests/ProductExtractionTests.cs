using FitMatrix.Api.Services;
using Xunit;

namespace FitMatrix.Api.Tests;

public sealed class ProductExtractionTests
{
    [Fact]
    public void ReadBodyDimensions_ParsesUnquotedStorefrontSpecLiterals()
    {
        const string html = """
            <script>window.__DATA__={specs:{spec:[{facet:"Height",value:"1.62 in (41.1 mm)",numeric:"1.62"},
            {facet:"Width",value:"3.12 in (79.2 mm)",numeric:"3.12"},{facet:"Length",value:"5.17 in (131.4 mm)"},
            {facet:"Cable length",value:"1.8 m"}]}}</script>
            """;

        var (width, height, depth) = OpenWebProductLookupService.ReadBodyDimensions(html);

        Assert.Equal(7.92m, width);
        Assert.Equal(4.11m, height);
        Assert.Equal(13.14m, depth);
    }

    [Fact]
    public void ReadBodyDimensions_ParsesQuotedLabelValuePairs()
    {
        const string html = """{"items":[{"label":"Width","value":"445 mm"},{"label":"Height","value":"38 mm"},{"label":"Depth","value":"135 mm"}]}""";

        var (width, height, depth) = OpenWebProductLookupService.ReadBodyDimensions(html);

        Assert.Equal(44.5m, width);
        Assert.Equal(3.8m, height);
        Assert.Equal(13.5m, depth);
    }

    [Fact]
    public void ReadBodyDimensions_ParsesJsonEscapedSpecListsAndIgnoresCableLength()
    {
        const string html = """
            {"specs":[{"value":"<ul><li>Length: 127.1 mm / 5.00 in</li><li>Width: 63.9 mm / 2.51 in</li><li>Height: 39.9 mm / 1.57 in</li><li>Cable Length: 1800 mm</li></ul>"}]}
            """;

        var (width, height, depth) = OpenWebProductLookupService.ReadBodyDimensions(html);

        Assert.Equal(6.39m, width);
        Assert.Equal(3.99m, height);
        Assert.Equal(12.71m, depth);
    }

    [Fact]
    public void ReadBodyDimensions_UsesTheLargestValueOfFrontAndBackMeasurements()
    {
        const string html = """
            "Length|||120.55mm / 4.75 inches","Width|||57.91mm / 2.28 inches (front), 67.03mm / 2.64 inches (back)","Height|||21.53mm / 0.85 inches (front), 37.98mm / 1.50 inches (back)"
            """;

        var (width, height, depth) = OpenWebProductLookupService.ReadBodyDimensions(html);

        Assert.Equal(6.703m, width);
        Assert.Equal(3.798m, height);
        Assert.Equal(12.055m, depth);
    }

    [Fact]
    public void ReadBodyDimensions_ParsesPhysicalDimensionTriples()
    {
        const string html = "<p>Phys. Dimension (W x H x D) : 61.50 x 45.50 x 21.40 cm (24.21\" x 17.91\" x 8.43\")</p>";

        var (width, height, depth) = OpenWebProductLookupService.ReadBodyDimensions(html, isDisplay: true);

        Assert.Equal(61.5m, width);
        Assert.Equal(45.5m, height);
        Assert.Equal(21.4m, depth);
    }

    [Theory]
    [InlineData("<meta property=\"og:title\" content=\"MX Master 3S Performance Wireless Mouse\">", "MX Master 3S Performance Wireless Mouse")]
    [InlineData("<title>Buy Razer Viper V3 Pro | Razer United States</title>", "Razer Viper V3 Pro")]
    [InlineData("<title>TUF Gaming VG27AQ1A｜Monitors｜ASUS USA</title>", "TUF Gaming VG27AQ1A")]
    [InlineData("<meta property=\"og:title\" content=\"Razer Viper V3 Pro - Sentinels Edition\">", "Razer Viper V3 Pro")]
    [InlineData("<meta property=\"og:title\" content=\"SteelSeries Aeon Pro™\">", "SteelSeries Aeon Pro")]
    [InlineData("<meta property=\"og:title\" content=\"SteelSeries Apex 3 US\">", "SteelSeries Apex 3")]
    public void ExtractPageProductName_StripsSiteDecorations(string html, string expected)
    {
        Assert.Equal(expected, OpenWebProductLookupService.ExtractPageProductName(html));
    }

    [Theory]
    [InlineData("Logitech MX Master 3S Performance Wireless Mouse", "/en-us/shop/p/mx-master-3s", "mouse")]
    [InlineData("Logitech G915 X", "/en-us/shop/p/g915-x-keyboards", "keyboard")]
    [InlineData("Logitech USB Unifying Receiver", "/en-us/shop/p/usb-unifying-receiver", null)]
    public void ClassifyPage_UsesTitleAndPathAndDropsGenericAccessories(string name, string path, string? expectedModel)
    {
        var candidate = new FitMatrix.Api.DTOs.ExternalProductCandidateDto("Resmî üretici web kataloğu", name, "Logitech", name, null, null,
            10, 4, 12, null, "https://www.logitech.com" + path, "official-structured", "", []);

        Assert.Equal(expectedModel, CatalogHarvestService.ClassifyPage(candidate, new Uri("https://www.logitech.com" + path))?.EditorModel);
    }

    [Theory]
    [InlineData("2021 ASUS TUF Gaming F15", "ASUS TUF Gaming F15 (2021)")]
    [InlineData("ASUS Vivobook 15", "ASUS Vivobook 15")]
    [InlineData("TUF Gaming VG27AQ1A", "ASUS TUF Gaming VG27AQ1A")]
    [InlineData("ROG ASUS Strix G16", "ASUS ROG Strix G16")]
    public void ComposeDisplayName_PutsTheBrandFirstOnce(string title, string expected)
    {
        Assert.Equal(expected, OpenWebProductLookupService.ComposeDisplayName("ASUS", title));
    }

    [Fact]
    public void PageVisitKey_FitsTheCacheKeyAndSurvivesStartupCleanup()
    {
        var key = CatalogHarvestService.PageVisitKey("https://www.asus.com/us/networking-iot-servers/whole-home-mesh-systems/aimesh-wifi-routers-and-systems/aimesh-ax6100-wifi-system-rt-ax92u-2-pack/techspec/");

        Assert.True(key.Length <= 180);
        Assert.StartsWith(ProductDiscoveryService.CacheVersion, key);
        Assert.NotEqual(key, CatalogHarvestService.PageVisitKey("https://www.asus.com/us/other/"));
    }

    [Fact]
    public void Robots_AppliesOnlyTheWildcardGroupAndWildcardPatterns()
    {
        const string robots = """
            User-agent: CazoodleBot
            Disallow: /

            User-agent: *
            Disallow: /*?*
            Disallow: */checkout
            Disallow: /api/
            """;

        var rules = ManufacturerSitemapReader.ParseDisallowRules(robots);

        Assert.DoesNotContain("/", rules);
        Assert.Contains(rules, rule => ManufacturerSitemapReader.RuleMatches(rule, "/gaming-mice/x?color=black"));
        Assert.Contains(rules, rule => ManufacturerSitemapReader.RuleMatches(rule, "/en-us/checkout"));
        Assert.DoesNotContain(rules, rule => ManufacturerSitemapReader.RuleMatches(rule, "/gaming-mice/razer-viper-v3-pro"));
    }

    [Theory]
    [InlineData("57.91mm / 2.28 inches (front), 67.03mm / 2.64 inches (back)", "67.03 mm")]
    [InlineData("1.62 in (41.1 mm)", "41.1 mm")]
    [InlineData("24.1 in / 61.2 cm", "61.2 cm")]
    [InlineData("41.1 mm", "41.1 mm")]
    [InlineData("1.62 in", "1.62 in")]
    public void PreferMetricValue_PicksTheMetricPartOfDualUnitValues(string value, string expected)
    {
        Assert.Equal(expected, OpenWebProductLookupService.PreferMetricValue(value));
    }

    [Theory]
    [InlineData("Whey Protein Isofort Baunilha Vitafor - 900g", "Vitafor", "Logitech G502 X Plus", false)]
    [InlineData("Genoise Chocolat Bijou", "Bijou", "Corsair K70 RGB PRO", false)]
    [InlineData("Logitech G502 X PLUS LIGHTSPEED Wireless Gaming Mouse", "Logitech", "Logitech G502 X Plus", true)]
    [InlineData("Corsair K70 RGB PRO Mechanical Gaming Keyboard", null, "Corsair K70 RGB PRO", true)]
    [InlineData("Herman Miller Aeron Chair Size B", "Herman Miller", "Herman Miller Aeron", true)]
    [InlineData("Any product", null, "0097855143130", true)]
    public void MatchesSpecificIdentity_RejectsBarcodeNoise(string title, string? brand, string query, bool expected)
    {
        Assert.Equal(expected, AccountFreeProductLookupService.MatchesSpecificIdentity(title, brand, query));
    }
}
