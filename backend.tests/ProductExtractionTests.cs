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

    [Fact]
    public void ReadBodyDimensions_ReadsLabelLinesAndSkipsParcelSizes()
    {
        // Shape of IKEA's rendered "Weight & measurements" section followed by the parcel list.
        const string html = """
            <h3>Weight &amp; measurements</h3>
            <dl><dt>Width</dt><dd>160 cm</dd><dt>Depth</dt><dd>80 cm</dd>
            <dt>Max. height</dt><dd>118 cm</dd><dt>Min. height</dt><dd>71 cm</dd></dl>
            <h3>Packaging</h3>
            <p>Article number 606.134.50</p><dl><dt>Width:</dt><dd>85 cm</dd><dt>Height:</dt><dd>3 cm</dd><dt>Length:</dt><dd>169 cm</dd></dl>
            """;

        var (width, height, depth) = OpenWebProductLookupService.ReadBodyDimensions(html);

        Assert.Equal((160m, 71m, 80m), (width!.Value, height!.Value, depth!.Value));
    }

    [Fact]
    public void ReadBodyDimensions_IgnoresUnitlessScriptValuesSuchAsImageSizes()
    {
        const string html = """
            <script>{images:[{key:"height",value:"1080"},{key:"width",value:"1920"}]}</script>
            <dl><dt>Product dimensions (L × W × H)</dt><dd>447 x 215 x 469 mm</dd></dl>
            """;

        var (width, height, depth) = OpenWebProductLookupService.ReadBodyDimensions(html);

        Assert.Equal((21.5m, 46.9m, 44.7m), (width!.Value, height!.Value, depth!.Value));
    }

    [Fact]
    public void ReadBodyDimensions_SkipsPackageTriplesAndHonoursLabelOrder()
    {
        const string html = """
            <script>{"dimensions":"12 x 34"}</script>
            <dl><dt>Product dimensions (L × W × H)</dt><dd>447 x 215 x 469 mm</dd>
            <dt>Package dimensions (L × W × H)</dt><dd>565 x 326 x 553 mm</dd></dl>
            """;

        var (width, height, depth) = OpenWebProductLookupService.ReadBodyDimensions(html);

        Assert.Equal(21.5m, width);
        Assert.Equal(46.9m, height);
        Assert.Equal(44.7m, depth);
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

    [Theory]
    [InlineData("IKEA MICKE desk, white, 105x50 cm", "IKEA MICKE desk, 105x50 cm")]
    [InlineData("IKEA MARKUS office chair, Vissle dark grey", "IKEA MARKUS office chair")]
    [InlineData("IKEA ALEX drawer unit", "IKEA ALEX drawer unit")]
    public void WithoutPlainSegments_MergesColourVariants(string name, string expected)
    {
        Assert.Equal(expected, ManufacturerSource.WithoutPlainSegments(name));
    }

    [Theory]
    [InlineData("https://www.ikea.com/gb/en/p/vadholma-drawer-unit-brown-stained-ash-50374332/", "IKEA VADHOLMA brown, stained ash, drawer unit", "IKEA VADHOLMA drawer unit")]
    [InlineData("https://www.ikea.com/gb/en/p/micke-desk-white-80354276/", "IKEA MICKE desk, white, 105x50 cm", "IKEA MICKE desk, 105x50 cm")]
    [InlineData("https://www.ikea.com/gb/en/p/lagkapten-alex-desk-white-s29521704/", "IKEA LAGKAPTEN / ALEX Desk, white, 140x60 cm", "IKEA LAGKAPTEN / ALEX desk, 140x60 cm")]
    [InlineData("https://www.ikea.com/gb/en/p/vattenkar-desktop-shelf-white-20541568/", "IKEA VATTENKAR Desktop shelf, white, 49x15 cm", "IKEA VATTENKAR desktop shelf, 49x15 cm")]
    public void NameFromUrl_UsesTheUrlTypeAndKeepsSizes(string url, string title, string expected)
    {
        var source = new ManufacturerSource("ikea", "IKEA", [], ".*",
            TypeFromUrl: "/p/[a-z0-9-]*?-(?<type>desk|drawer-unit|desktop-shelf)-");

        Assert.Equal(expected, source.NameFromUrl(new Uri(url), title));
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
