using FitMatrix.Api.Services;
using Xunit;

namespace FitMatrix.Api.Tests;

public sealed class ProductGuruLookupTests
{
    [Theory]
    [InlineData("Logitech MX Mechanical Wireless Keyboard", "keyboard", true)]
    [InlineData("Keyboard dust cover for Logitech", "keyboard", false)]
    [InlineData("Gaming Mouse Pad for Computer Keyboard", "keyboard", false)]
    [InlineData("Razer DeathAdder V3 Wired Gaming Mouse 30000 DPI", "mouse", true)]
    [InlineData("3D Mouse Mat with Wrist Support", "mouse", false)]
    [InlineData("Gaming Mouse pads RGB extended desk mat", "mouse", false)]
    [InlineData("Mickey Mouse DVD", "mouse", false)]
    [InlineData("LG UltraGear 27 inch Gaming Monitor", "monitor", true)]
    [InlineData("Dual monitor arm stand", "monitor", false)]
    public void MatchesEquipmentCategory_RemovesAccessoryAndKeywordNoise(string title, string query, bool expected)
    {
        Assert.Equal(expected, ProductGuruLookupService.MatchesEquipmentCategory(title, query));
    }
}
