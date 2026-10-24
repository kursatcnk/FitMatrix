using System.Text.RegularExpressions;

namespace FitMatrix.Api.Services;

public sealed record ProductClassification(string CategorySlug, string EditorModel, string DisplayColor);

public static class ProductTaxonomy
{
    public static readonly HashSet<string> SupportedCategories =
    [
        "display", "computer", "input", "audio", "imaging", "network", "gaming", "mobile",
        "furniture", "lighting", "power", "office", "accessory"
    ];

    public static readonly HashSet<string> SupportedEditorModels =
    [
        "monitor", "tower", "keyboard", "mouse", "laptop", "speaker", "headphone", "mat", "lamp", "stand",
        "desk", "chair", "router", "camera", "printer", "phone", "tablet", "console", "power", "generic"
    ];

    private static readonly (Regex Pattern, ProductClassification Classification)[] Rules =
    [
        Rule(@"\b(mouse\s*pad|desk\s*mat|mousepad)\b", "accessory", "mat", "#46514f"),
        Rule(@"\b(monitor\s*(?:stand|arm|riser|mount)|screen\s*(?:stand|arm|riser|mount))\b", "accessory", "stand", "#675f52"),
        Rule(@"\b(monitors?|monitör|displays?|screen|ekran|television|projectors?|projeksiyon)\b", "display", "monitor", "#252a2e"),
        Rule(@"\b(laptops?|notebooks?|ultrabooks?|dizüstü)\b", "computer", "laptop", "#596368"),
        Rule(@"\b(computer\s*case|pc\s*case|chassis|tower|bilgisayar\s*kasası|kasa|desktop\s*pc|mini\s*pc)\b", "computer", "tower", "#30383a"),
        Rule(@"\b(keyboards?|klavye|keypads?|macro\s*pads?)\b", "input", "keyboard", "#3d4448"),
        Rule(@"\b(mouse|mice|fare|trackballs?|touchpads?|stylus|drawing\s*tablets?|joysticks?|gamepads?|controllers?)\b", "input", "mouse", "#3d4448"),
        Rule(@"\b(headphones?|headsets?|earphones?|earbuds?|kulaklık)\b", "audio", "headphone", "#343b40"),
        Rule(@"\b(speakers?|hoparlör|soundbars?|microphones?|mikrofon|dac|amplifiers?)\b", "audio", "speaker", "#343b40"),
        Rule(@"\b(webcams?|cameras?|kamera|capture\s*cards?|action\s*cams?|camcorders?)\b", "imaging", "camera", "#3b4248"),
        Rule(@"\b(router|modem|ethernet\s*switch|network\s*switch|access\s*point|mesh\s*wifi|wi-?fi|nas)\b", "network", "router", "#3a464b"),
        Rule(@"\b(playstation|xbox|nintendo|console|konsol|steam\s*deck|handheld|vr\s*headset|virtual\s*reality|racing\s*wheel)\b", "gaming", "console", "#34384a"),
        Rule(@"\b(tablet|ipad|e-?reader|kindle)\b", "mobile", "tablet", "#46505a"),
        Rule(@"\b(smartphone|phone|telefon|smartwatch|akıllı\s*saat)\b", "mobile", "phone", "#46505a"),
        Rule(@"\b(chairs?|sandalye|office\s*chairs?|gaming\s*chairs?)\b", "furniture", "chair", "#655c50"),
        Rule(@"\b(desk|table|masa|shelf|raf|cabinet|dolap|drawer|çekmece|bookcase)\b", "furniture", "desk", "#756752"),
        Rule(@"\b(lamp|light|lighting|lamba|aydınlatma|light\s*bar|led\s*strip)\b", "lighting", "lamp", "#b5a57d"),
        Rule(@"\b(ups|power\s*supply|power\s*strip|surge\s*protector|charger|şarj|adapter|battery|batarya)\b", "power", "power", "#4a4d50"),
        Rule(@"\b(printer|yazıcı|scanner|tarayıcı|label\s*maker|laminator|shredder)\b", "office", "printer", "#606468"),
        Rule(@"\b(stand|riser|mount|arm|bracket|yükseltici|tutucu)\b", "accessory", "stand", "#675f52"),
        Rule(@"\b(dock|docking|hub|cable|kablo|converter|çevirici|external\s*drive|ssd|hard\s*drive)\b", "accessory", "generic", "#4b5356")
    ];

    // Manufacturers disagree on axis names for devices that lie flat: Logitech calls a keyboard's front-to-back
    // size "height" and its thickness "depth". On the desk the thinnest side is always vertical; keyboards,
    // laptops, mats and tablets are widest left to right, mice and phones are longest front to back.
    public static (decimal Width, decimal Height, decimal Depth) OrientFlatDevice(string editorModel, decimal width, decimal height, decimal depth)
    {
        var lengthwise = editorModel is "mouse" or "phone";
        if (!lengthwise && editorModel is not ("keyboard" or "laptop" or "mat" or "tablet")) return (width, height, depth);
        var sorted = new[] { width, height, depth }.Order().ToArray();
        return lengthwise ? (sorted[1], sorted[0], sorted[2]) : (sorted[2], sorted[0], sorted[1]);
    }

    private static readonly Dictionary<string, decimal> LongestSideLimitCm = new()
    {
        ["mouse"] = 25, ["phone"] = 25, ["headphone"] = 40, ["camera"] = 40, ["tablet"] = 45, ["laptop"] = 50,
        ["router"] = 70, ["console"] = 70, ["power"] = 80, ["keyboard"] = 90, ["tower"] = 100, ["printer"] = 100,
        ["lamp"] = 150, ["speaker"] = 150, ["mat"] = 150, ["stand"] = 150, ["monitor"] = 150, ["chair"] = 160,
        ["desk"] = 300, ["generic"] = 200
    };

    // Catches a cable length or packaging size read as a body dimension (a 177 cm headset).
    public static bool IsPlausibleSize(string editorModel, decimal width, decimal height, decimal depth) =>
        !LongestSideLimitCm.TryGetValue(editorModel, out var limit) || Math.Max(width, Math.Max(height, depth)) <= limit;

    // Used when a curated source already knows the product family; keeps colours consistent with Classify.
    public static ProductClassification? FromHint(string category, string editorModel)
    {
        if (!SupportedCategories.Contains(category) || !SupportedEditorModels.Contains(editorModel)) return null;
        var known = Rules.Select(rule => rule.Classification)
            .FirstOrDefault(classification => classification.CategorySlug == category && classification.EditorModel == editorModel);
        return known ?? new ProductClassification(category, editorModel, "#3b4240");
    }

    public static ProductClassification Classify(string text)
    {
        foreach (var rule in Rules)
            if (rule.Pattern.IsMatch(text)) return rule.Classification;
        return new ProductClassification("accessory", "generic", "#3b4240");
    }

    private static (Regex, ProductClassification) Rule(
        string pattern,
        string category,
        string model,
        string color) =>
        (new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)),
            new ProductClassification(category, model, color));
}
