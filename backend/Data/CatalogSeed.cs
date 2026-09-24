using FitMatrix.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FitMatrix.Api.Data;

public static class CatalogSeed
{
    private static readonly Guid GenericBrandId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset SeedTime = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

    public static void Configure(ModelBuilder modelBuilder)
    {
        var categories = new[]
        {
            Category("20000000-0000-0000-0000-000000000001", "Ekran", "display", "monitor"),
            Category("20000000-0000-0000-0000-000000000002", "Bilgisayar", "computer", "tower"),
            Category("20000000-0000-0000-0000-000000000003", "Kontrol", "input", "keyboard"),
            Category("20000000-0000-0000-0000-000000000004", "Ses", "audio", "speaker"),
            Category("20000000-0000-0000-0000-000000000005", "Aksesuar", "accessory", "mat"),
            Category("20000000-0000-0000-0000-000000000006", "Görüntü", "imaging", "camera"),
            Category("20000000-0000-0000-0000-000000000007", "Ağ", "network", "router"),
            Category("20000000-0000-0000-0000-000000000008", "Oyun", "gaming", "console"),
            Category("20000000-0000-0000-0000-000000000009", "Mobil", "mobile", "phone"),
            Category("20000000-0000-0000-0000-000000000010", "Mobilya", "furniture", "desk"),
            Category("20000000-0000-0000-0000-000000000011", "Aydınlatma", "lighting", "lamp"),
            Category("20000000-0000-0000-0000-000000000012", "Güç", "power", "power"),
            Category("20000000-0000-0000-0000-000000000013", "Ofis", "office", "printer")
        };

        modelBuilder.Entity<Brand>().HasData(new Brand
        {
            Id = GenericBrandId,
            Name = "FitMatrix Generic",
            Slug = "fitmatrix-generic"
        });
        modelBuilder.Entity<EquipmentCategory>().HasData(categories);

        modelBuilder.Entity<EquipmentProduct>().HasData(
            Product("30000000-0000-0000-0000-000000000001", categories[0].Id, "27-inch-monitor", "27” Monitör", "monitor", 61, 46, 20, 24, 20, "#252a2e"),
            Product("30000000-0000-0000-0000-000000000002", categories[0].Id, "24-inch-monitor", "24” Monitör", "monitor", 54, 41, 18, 22, 18, "#343a3f"),
            Product("30000000-0000-0000-0000-000000000003", categories[1].Id, "mid-tower", "Mid Tower Kasa", "tower", 22, 47, 46, 22, 46, "#30383a"),
            Product("30000000-0000-0000-0000-000000000004", categories[1].Id, "14-inch-laptop", "14” Laptop", "laptop", 32, 20, 22, 32, 22, "#667076"),
            Product("30000000-0000-0000-0000-000000000005", categories[2].Id, "tkl-keyboard", "TKL Klavye", "keyboard", 36, 3, 14, 36, 14, "#444b50"),
            Product("30000000-0000-0000-0000-000000000006", categories[2].Id, "mouse", "Mouse", "mouse", 7, 4, 12, 7, 12, "#3d4448"),
            Product("30000000-0000-0000-0000-000000000007", categories[3].Id, "desktop-speaker", "Masaüstü Hoparlör", "speaker", 12, 22, 14, 12, 14, "#2e3437"),
            Product("30000000-0000-0000-0000-000000000008", categories[4].Id, "desk-mat", "Geniş Mousepad", "mat", 80, .4m, 30, 80, 30, "#4f5f5b"),
            Product("30000000-0000-0000-0000-000000000009", categories[4].Id, "desk-lamp", "Masa Lambası", "lamp", 16, 48, 16, 16, 16, "#b5a57d"),
            Product("30000000-0000-0000-0000-000000000010", categories[0].Id, "32-inch-monitor", "32” Monitör", "monitor", 72, 51, 24, 28, 24, "#202629"),
            Product("30000000-0000-0000-0000-000000000011", categories[0].Id, "34-inch-ultrawide", "34” Ultrawide Monitör", "monitor", 81, 47, 25, 31, 25, "#2a3033"),
            Product("30000000-0000-0000-0000-000000000012", categories[1].Id, "full-tower", "Full Tower Kasa", "tower", 24, 52, 51, 24, 51, "#242a2d"),
            Product("30000000-0000-0000-0000-000000000013", categories[1].Id, "mini-pc", "Mini PC", "tower", 13, 5, 13, 13, 13, "#596064"),
            Product("30000000-0000-0000-0000-000000000014", categories[1].Id, "15-inch-laptop", "15.6” Laptop", "laptop", 36, 23, 24, 36, 24, "#596368"),
            Product("30000000-0000-0000-0000-000000000015", categories[2].Id, "full-size-keyboard", "Tam Boy Klavye", "keyboard", 44, 3, 14, 44, 14, "#383e42"),
            Product("30000000-0000-0000-0000-000000000016", categories[4].Id, "monitor-riser", "Monitör Yükseltici", "stand", 55, 10, 24, 55, 24, "#756752")
        );

        modelBuilder.Entity<AttributeDefinition>().HasData(
            Attribute("40000000-0000-0000-0000-000000000001", categories[0].Id, "diagonal_in", "Ekran boyutu", "number", "in", true),
            Attribute("40000000-0000-0000-0000-000000000002", categories[0].Id, "refresh_rate_hz", "Yenileme hızı", "number", "Hz", true),
            Attribute("40000000-0000-0000-0000-000000000003", categories[0].Id, "panel_type", "Panel türü", "text", null, true),
            Attribute("40000000-0000-0000-0000-000000000004", categories[1].Id, "max_gpu_length_mm", "Maksimum ekran kartı uzunluğu", "number", "mm", true),
            Attribute("40000000-0000-0000-0000-000000000005", categories[1].Id, "form_factor", "Form faktörü", "text", null, true),
            Attribute("40000000-0000-0000-0000-000000000006", categories[2].Id, "connection_type", "Bağlantı türü", "text", null, true),
            Attribute("40000000-0000-0000-0000-000000000007", categories[2].Id, "layout", "Klavye düzeni", "text", null, true)
        );
    }

    private static EquipmentCategory Category(string id, string name, string slug, string editorModel) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        Slug = slug,
        EditorModel = editorModel
    };

    private static EquipmentProduct Product(string id, Guid categoryId, string modelName, string displayName, string editorModel, decimal width, decimal height, decimal depth, decimal footprintWidth, decimal footprintDepth, string color) => new()
    {
        Id = Guid.Parse(id),
        BrandId = GenericBrandId,
        CategoryId = categoryId,
        ModelName = modelName,
        DisplayName = displayName,
        WidthCm = width,
        HeightCm = height,
        DepthCm = depth,
        FootprintWidthCm = footprintWidth,
        FootprintDepthCm = footprintDepth,
        EditorModel = editorModel,
        DisplayColor = color,
        IsVerified = false,
        DataQuality = "template",
        CreatedAt = SeedTime,
        UpdatedAt = SeedTime
    };

    private static AttributeDefinition Attribute(string id, Guid categoryId, string code, string name, string valueType, string? unit, bool comparable) => new()
    {
        Id = Guid.Parse(id),
        CategoryId = categoryId,
        Code = code,
        Name = name,
        ValueType = valueType,
        Unit = unit,
        IsComparable = comparable,
        IsRequired = false
    };
}
