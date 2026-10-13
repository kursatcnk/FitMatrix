using System.Security.Cryptography;
using System.Text;
using FitMatrix.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FitMatrix.Api.Data;

/// <summary>
/// Adds a useful, source-linked local baseline without turning the catalog into a
/// hand-maintained product dump. New products discovered on the web still flow
/// through the normal import pipeline.
/// </summary>
public static class ExpandedCatalogSeed
{
    private const string DataQuality = "manufacturer-curated-v1";

    private static readonly IReadOnlyDictionary<string, (string Name, string Website)> Brands =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["aoc"] = ("AOC", "https://www.aoc.com/"),
            ["asus"] = ("ASUS", "https://www.asus.com/"),
            ["benq"] = ("BenQ", "https://www.benq.com/"),
            ["be-quiet"] = ("be quiet!", "https://www.bequiet.com/"),
            ["cooler-master"] = ("Cooler Master", "https://www.coolermaster.com/"),
            ["corsair"] = ("Corsair", "https://www.corsair.com/"),
            ["dell"] = ("Dell", "https://www.dell.com/"),
            ["fractal-design"] = ("Fractal Design", "https://www.fractal-design.com/"),
            ["glorious"] = ("Glorious", "https://www.gloriousgaming.com/"),
            ["hyperx"] = ("HyperX", "https://hyperx.com/"),
            ["ikea"] = ("IKEA", "https://www.ikea.com/"),
            ["keychron"] = ("Keychron", "https://www.keychron.com/"),
            ["lg"] = ("LG", "https://www.lg.com/"),
            ["lian-li"] = ("Lian Li", "https://lian-li.com/"),
            ["logitech"] = ("Logitech", "https://www.logitech.com/"),
            ["msi"] = ("MSI", "https://www.msi.com/"),
            ["nuphy"] = ("NuPhy", "https://nuphy.com/"),
            ["nzxt"] = ("NZXT", "https://nzxt.com/"),
            ["pulsar"] = ("Pulsar", "https://www.pulsar.gg/"),
            ["razer"] = ("Razer", "https://www.razer.com/"),
            ["samsung"] = ("Samsung", "https://www.samsung.com/"),
            ["secretlab"] = ("Secretlab", "https://secretlab.co/"),
            ["steelseries"] = ("SteelSeries", "https://steelseries.com/"),
            ["thermaltake"] = ("Thermaltake", "https://www.thermaltake.com/"),
            ["viewsonic"] = ("ViewSonic", "https://www.viewsonic.com/"),
            ["wooting"] = ("Wooting", "https://wooting.io/")
        };

    private static readonly CatalogItem[] Products =
    [
        // Keyboards
        Item("logitech", "input", "MX Keys S", "Logitech MX Keys S", 43.02m, 2.05m, 13.16m, .81m, "keyboard", "https://www.logitech.com/products/keyboards/mx-keys-s.html"),
        Item("logitech", "input", "MX Mechanical", "Logitech MX Mechanical", 43.36m, 2.61m, 13.17m, .83m, "keyboard", "https://www.logitech.com/products/keyboards/mx-mechanical.html"),
        Item("logitech", "input", "G915 TKL", "Logitech G915 TKL LIGHTSPEED", 36.8m, 2.2m, 15m, .81m, "keyboard", "https://www.logitechg.com/products/gaming-keyboards/g915-tkl-wireless.html"),
        Item("keychron", "input", "Q1 Max", "Keychron Q1 Max", 32.75m, 3.56m, 14.5m, 1.72m, "keyboard", "https://www.keychron.com/products/keychron-q1-max-qmk-via-wireless-custom-mechanical-keyboard"),
        Item("keychron", "input", "K2 Pro", "Keychron K2 Pro", 31.71m, 4.07m, 12.98m, 1.07m, "keyboard", "https://www.keychron.com/products/keychron-k2-pro-qmk-via-wireless-mechanical-keyboard"),
        Item("razer", "input", "BlackWidow V4 Pro", "Razer BlackWidow V4 Pro", 46.6m, 4.39m, 15.25m, 1.64m, "keyboard", "https://www.razer.com/gaming-keyboards/razer-blackwidow-v4-pro"),
        Item("razer", "input", "Huntsman V3 Pro TKL", "Razer Huntsman V3 Pro TKL", 36.3m, 3.9m, 13.9m, .72m, "keyboard", "https://www.razer.com/gaming-keyboards/razer-huntsman-v3-pro-tenkeyless"),
        Item("corsair", "input", "K70 RGB PRO", "Corsair K70 RGB PRO", 44.4m, 4m, 16.6m, 1.15m, "keyboard", "https://www.corsair.com/p/keyboards/ch-9109410-na/k70-rgb-pro-mechanical-gaming-keyboard"),
        Item("steelseries", "input", "Apex Pro TKL Gen 3", "SteelSeries Apex Pro TKL Gen 3", 35.5m, 4.2m, 12.8m, 1.03m, "keyboard", "https://steelseries.com/gaming-keyboards/apex-pro-tkl-gen-3"),
        Item("hyperx", "input", "Alloy Origins Core", "HyperX Alloy Origins Core", 36m, 3.45m, 13.25m, .9m, "keyboard", "https://hyperx.com/products/hyperx-alloy-origins-core-mechanical-gaming-keyboard"),
        Item("wooting", "input", "60HE+", "Wooting 60HE+", 30.2m, 3.8m, 11.6m, .61m, "keyboard", "https://wooting.io/wooting-60he"),
        Item("nuphy", "input", "Air75 V2", "NuPhy Air75 V2", 31.64m, 1.35m, 13.25m, .6m, "keyboard", "https://nuphy.com/products/air75-v2"),

        // Mice
        Item("logitech", "input", "MX Master 3S", "Logitech MX Master 3S", 8.43m, 5.1m, 12.49m, .141m, "mouse", "https://www.logitech.com/products/mice/mx-master-3s.html"),
        Item("logitech", "input", "G PRO X SUPERLIGHT 2", "Logitech G PRO X SUPERLIGHT 2", 6.35m, 4m, 12.5m, .06m, "mouse", "https://www.logitechg.com/products/gaming-mice/pro-x2-superlight-wireless-mouse.html"),
        Item("logitech", "input", "G502 X PLUS", "Logitech G502 X PLUS", 7.91m, 4.11m, 13.14m, .106m, "mouse", "https://www.logitechg.com/products/gaming-mice/g502-x-plus-wireless-lightforce.html"),
        Item("razer", "input", "DeathAdder V3 Pro", "Razer DeathAdder V3 Pro", 6.8m, 4.4m, 12.8m, .063m, "mouse", "https://www.razer.com/gaming-mice/razer-deathadder-v3-pro"),
        Item("razer", "input", "Viper V3 Pro", "Razer Viper V3 Pro", 6.38m, 3.99m, 12.71m, .054m, "mouse", "https://www.razer.com/gaming-mice/razer-viper-v3-pro"),
        Item("steelseries", "input", "Aerox 3 Wireless", "SteelSeries Aerox 3 Wireless", 6.7m, 3.8m, 12.06m, .068m, "mouse", "https://steelseries.com/gaming-mice/aerox-3-wireless-2022"),
        Item("corsair", "input", "M75 AIR", "Corsair M75 AIR Wireless", 6.5m, 4.16m, 12.8m, .06m, "mouse", "https://www.corsair.com/p/gaming-mouse/ch-931d100-na/m75-air-wireless-ultra-lightweight-gaming-mouse"),
        Item("hyperx", "input", "Pulsefire Haste 2", "HyperX Pulsefire Haste 2", 6.68m, 3.82m, 12.43m, .053m, "mouse", "https://hyperx.com/products/hyperx-pulsefire-haste-2-gaming-mouse"),
        Item("pulsar", "input", "X2V2 Medium", "Pulsar X2V2 Medium", 6.3m, 3.8m, 12m, .052m, "mouse", "https://www.pulsar.gg/products/x2v2-gaming-mouse"),
        Item("glorious", "input", "Model O", "Glorious Model O", 6.6m, 3.75m, 12.8m, .067m, "mouse", "https://www.gloriousgaming.com/products/glorious-model-o-white"),

        // Monitors (overall dimensions with stand; conservative footprint where the base width is not published)
        Item("msi", "display", "MAG 274QF", "MSI MAG 274QF 27-inch", 61.35m, 47.28m, 22.75m, 5.7m, "monitor", "https://www.msi.com/Monitor/MAG-274QF/Specification"),
        Item("msi", "display", "PRO MP275Q E14", "MSI PRO MP275Q E14 27-inch", 61.35m, 45.72m, 20.68m, 3.95m, "monitor", "https://www.msi.com/Business-Productivity-Monitor/PRO-MP275Q-E14/Specification"),
        Item("aoc", "display", "Q27G3XMN", "AOC Q27G3XMN 27-inch", 61.45m, 51.96m, 30.16m, 5.44m, "monitor", "https://www.aoc.com/us/gaming/products/monitors/q27g3xmn"),
        Item("dell", "display", "U2723QE", "Dell UltraSharp U2723QE 27-inch", 61.14m, 53.52m, 18.5m, 6.64m, "monitor", "https://www.dell.com/en-us/shop/dell-ultrasharp-27-4k-usb-c-hub-monitor-u2723qe/apd/210-bdpf/monitors-monitor-accessories"),
        Item("lg", "display", "27GR83Q-B", "LG UltraGear 27GR83Q-B 27-inch", 61.35m, 57.73m, 25.35m, 5.9m, "monitor", "https://www.lg.com/us/monitors/lg-27gr83q-b-gaming-monitor"),
        Item("samsung", "display", "C32G75T", "Samsung Odyssey G7 C32G75T 32-inch", 71.01m, 59.44m, 30.59m, 8.2m, "monitor", "https://www.samsung.com/us/computing/monitors/gaming/32-odyssey-g7-gaming-monitor-lc32g75tqsnxza/"),
        Item("asus", "display", "PG27AQDM", "ASUS ROG Swift OLED PG27AQDM 27-inch", 60.5m, 54.8m, 27.4m, 6.9m, "monitor", "https://rog.asus.com/monitors/27-to-31-5-inches/rog-swift-oled-pg27aqdm/"),
        Item("benq", "display", "PD2705Q", "BenQ DesignVue PD2705Q 27-inch", 61.4m, 54.9m, 23m, 8.5m, "monitor", "https://www.benq.com/en-us/monitor/professional/pd2705q/spec.html"),
        Item("viewsonic", "display", "VX2728J-2K", "ViewSonic VX2728J-2K 27-inch", 61.5m, 43.8m, 20.7m, 4.7m, "monitor", "https://www.viewsonic.com/global/products/lcd/VX2728J-2K"),

        // PC cases
        Item("asus", "computer", "A31 PLUS TG ARGB", "ASUS A31 PLUS TG ARGB", 23.8m, 51.1m, 43.5m, 9.1m, "tower", "https://www.asus.com/motherboards-components/gaming-cases/asus/asus-a31-plus-case/techspec/"),
        Item("corsair", "computer", "4000D AIRFLOW", "Corsair 4000D AIRFLOW", 23m, 46.6m, 45.3m, 7.85m, "tower", "https://www.corsair.com/p/pc-cases/cc-9011200-ww/4000d-airflow-tempered-glass-mid-tower-atx-case-black"),
        Item("nzxt", "computer", "H5 Flow", "NZXT H5 Flow", 22.7m, 46.4m, 44.6m, 7.01m, "tower", "https://nzxt.com/product/h5-flow"),
        Item("fractal-design", "computer", "North", "Fractal Design North", 21.5m, 46.9m, 44.7m, 7.6m, "tower", "https://www.fractal-design.com/products/cases/north/north/charcoal-black-tg-dark/"),
        Item("lian-li", "computer", "O11 Dynamic EVO", "Lian Li O11 Dynamic EVO", 28.5m, 45.9m, 46.5m, 12.7m, "tower", "https://lian-li.com/product/o11-dynamic-evo/"),
        Item("cooler-master", "computer", "MasterBox NR200P", "Cooler Master MasterBox NR200P", 18.5m, 29.2m, 37.6m, 5.1m, "tower", "https://www.coolermaster.com/en-global/products/masterbox-nr200p/"),
        Item("be-quiet", "computer", "Pure Base 500DX", "be quiet! Pure Base 500DX", 23.2m, 46.3m, 45m, 7.8m, "tower", "https://www.bequiet.com/en/case/1854"),
        Item("thermaltake", "computer", "Ceres 300 TG ARGB", "Thermaltake Ceres 300 TG ARGB", 24.5m, 47.5m, 46.3m, 7.8m, "tower", "https://www.thermaltake.com/ceres-300-tg-argb-mid-tower-chassis.html"),

        // Desks. In the editor depth is the work-surface depth and height is the desk height.
        Item("ikea", "furniture", "PAHL 128x58", "IKEA PÅHL Desk 128x58", 128m, 72m, 58m, 22m, "desk", "https://www.ikea.com/us/en/p/pahl-desk-white-s49128945/"),
        Item("ikea", "furniture", "MICKE 105x50", "IKEA MICKE Desk 105x50", 105m, 75m, 50m, 27.5m, "desk", "https://www.ikea.com/us/en/p/micke-desk-white-30213076/"),
        Item("ikea", "furniture", "UTESPELARE 160x80", "IKEA UTESPELARE Gaming Desk 160x80", 160m, 78m, 80m, 31.5m, "desk", "https://www.ikea.com/us/en/p/utespelare-gaming-desk-black-80507627/"),
        Item("secretlab", "furniture", "MAGNUS Pro", "Secretlab MAGNUS Pro Desk", 150m, 125m, 70m, 57m, "desk", "https://secretlab.co/products/magnus-pro")
    ];

    public static async Task SeedAsync(FitMatrixDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (await dbContext.EquipmentProducts.AnyAsync(product => product.DataQuality == DataQuality, cancellationToken)) return;

        var categories = await dbContext.EquipmentCategories
            .ToDictionaryAsync(category => category.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var existingBrands = await dbContext.Brands
            .ToDictionaryAsync(brand => brand.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var (slug, definition) in Brands)
        {
            if (existingBrands.ContainsKey(slug)) continue;
            var brand = new Brand
            {
                Id = StableGuid($"brand:{slug}"),
                Name = definition.Name,
                Slug = slug,
                WebsiteUrl = definition.Website
            };
            dbContext.Brands.Add(brand);
            existingBrands.Add(slug, brand);
        }

        var productIds = Products.Select(product => StableGuid($"product:{product.BrandSlug}:{product.ModelName}")).ToArray();
        var existingIds = await dbContext.EquipmentProducts
            .Where(product => productIds.Contains(product.Id))
            .Select(product => product.Id)
            .ToHashSetAsync(cancellationToken);
        var existingIdentityRows = await dbContext.EquipmentProducts
            .AsNoTracking()
            .Select(product => new { product.BrandId, product.ManufacturerPartNumber, product.ModelName })
            .ToListAsync(cancellationToken);
        var existingIdentityKeys = existingIdentityRows
            .Select(product => IdentityKey(
                product.BrandId,
                string.IsNullOrWhiteSpace(product.ManufacturerPartNumber) ? product.ModelName : product.ManufacturerPartNumber))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        foreach (var definition in Products)
        {
            var productId = StableGuid($"product:{definition.BrandSlug}:{definition.ModelName}");
            var brandId = existingBrands[definition.BrandSlug].Id;
            if (existingIds.Contains(productId) ||
                existingIdentityKeys.Contains(IdentityKey(brandId, definition.ModelName)) ||
                !categories.TryGetValue(definition.CategorySlug, out var category)) continue;

            var product = new EquipmentProduct
            {
                Id = productId,
                BrandId = brandId,
                CategoryId = category.Id,
                DisplayName = definition.DisplayName,
                ModelName = definition.ModelName,
                ManufacturerPartNumber = definition.ModelName,
                WidthCm = definition.WidthCm,
                HeightCm = definition.HeightCm,
                DepthCm = definition.DepthCm,
                FootprintWidthCm = definition.WidthCm,
                FootprintDepthCm = definition.DepthCm,
                WeightKg = definition.WeightKg,
                EditorModel = definition.EditorModel,
                DisplayColor = ColorFor(definition.EditorModel),
                IsVerified = false,
                DataQuality = DataQuality,
                CreatedAt = now,
                UpdatedAt = now
            };
            product.Sources.Add(new ProductSource
            {
                Id = StableGuid($"source:{definition.BrandSlug}:{definition.ModelName}"),
                ProductId = productId,
                Provider = "Manufacturer",
                SourceUrl = definition.SourceUrl,
                ExternalId = definition.ModelName,
                LicenseName = "Official product specification reference",
                LastCheckedAt = now
            });
            dbContext.EquipmentProducts.Add(product);
            existingIdentityKeys.Add(IdentityKey(brandId, definition.ModelName));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static CatalogItem Item(
        string brandSlug,
        string categorySlug,
        string modelName,
        string displayName,
        decimal widthCm,
        decimal heightCm,
        decimal depthCm,
        decimal weightKg,
        string editorModel,
        string sourceUrl) =>
        new(brandSlug, categorySlug, modelName, displayName, widthCm, heightCm, depthCm, weightKg, editorModel, sourceUrl);

    private static Guid StableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"fitmatrix:{value.ToLowerInvariant()}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string IdentityKey(Guid brandId, string value) =>
        $"{brandId:N}:{new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray())}";

    private static string ColorFor(string editorModel) => editorModel switch
    {
        "monitor" => "#252a2e",
        "tower" => "#30383a",
        "keyboard" => "#3d4448",
        "mouse" => "#444b50",
        "desk" => "#756752",
        _ => "#3b4240"
    };

    private sealed record CatalogItem(
        string BrandSlug,
        string CategorySlug,
        string ModelName,
        string DisplayName,
        decimal WidthCm,
        decimal HeightCm,
        decimal DepthCm,
        decimal WeightKg,
        string EditorModel,
        string SourceUrl);
}
