using FitMatrix.Api.Services;
using Xunit;

namespace FitMatrix.Api.Tests;

public sealed class ProductTaxonomyTests
{
    [Theory]
    [InlineData("MSI MAG 274QF gaming monitor", "display", "monitor")]
    [InlineData("Corsair 4000D computer case", "computer", "tower")]
    [InlineData("Sony WH-1000XM5 headphones", "audio", "headphone")]
    [InlineData("Logitech Brio webcam", "imaging", "camera")]
    [InlineData("TP-Link Archer AX55 router", "network", "router")]
    [InlineData("IKEA MICKE desk", "furniture", "desk")]
    [InlineData("Herman Miller Aeron office chair", "furniture", "chair")]
    [InlineData("Canon Pixma printer", "office", "printer")]
    [InlineData("Apple iPad tablet", "mobile", "tablet")]
    [InlineData("Samsung Galaxy phone", "mobile", "phone")]
    public void Classify_MapsBroadProductFamilies(string input, string category, string editorModel)
    {
        var result = ProductTaxonomy.Classify(input);

        Assert.Equal(category, result.CategorySlug);
        Assert.Equal(editorModel, result.EditorModel);
    }

    [Fact]
    public void Classify_PrefersMonitorAccessoryOverDisplay()
    {
        var result = ProductTaxonomy.Classify("Ergotron monitor arm");

        Assert.Equal("accessory", result.CategorySlug);
        Assert.Equal("stand", result.EditorModel);
    }
}
