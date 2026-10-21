using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FitMatrix.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandStarterCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "EquipmentProducts",
                columns: new[] { "Id", "BrandId", "CategoryId", "CreatedAt", "DataQuality", "DepthCm", "DisplayColor", "DisplayName", "EditorModel", "FootprintDepthCm", "FootprintWidthCm", "Gtin", "HeightCm", "ImageUrl", "IsVerified", "ManufacturerPartNumber", "ModelName", "UpdatedAt", "WeightKg", "WidthCm" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000010"), new Guid("10000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "template", 24m, "#202629", "32” Monitör", "monitor", 24m, 28m, null, 51m, null, false, null, "32-inch-monitor", new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 72m },
                    { new Guid("30000000-0000-0000-0000-000000000011"), new Guid("10000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "template", 25m, "#2a3033", "34” Ultrawide Monitör", "monitor", 25m, 31m, null, 47m, null, false, null, "34-inch-ultrawide", new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 81m },
                    { new Guid("30000000-0000-0000-0000-000000000012"), new Guid("10000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "template", 51m, "#242a2d", "Full Tower Kasa", "tower", 51m, 24m, null, 52m, null, false, null, "full-tower", new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 24m },
                    { new Guid("30000000-0000-0000-0000-000000000013"), new Guid("10000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "template", 13m, "#596064", "Mini PC", "tower", 13m, 13m, null, 5m, null, false, null, "mini-pc", new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 13m },
                    { new Guid("30000000-0000-0000-0000-000000000014"), new Guid("10000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "template", 24m, "#596368", "15.6” Laptop", "laptop", 24m, 36m, null, 23m, null, false, null, "15-inch-laptop", new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 36m },
                    { new Guid("30000000-0000-0000-0000-000000000015"), new Guid("10000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "template", 14m, "#383e42", "Tam Boy Klavye", "keyboard", 14m, 44m, null, 3m, null, false, null, "full-size-keyboard", new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 44m },
                    { new Guid("30000000-0000-0000-0000-000000000016"), new Guid("10000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000005"), new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "template", 24m, "#756752", "Monitör Yükseltici", "stand", 24m, 55m, null, 10m, null, false, null, "monitor-riser", new DateTimeOffset(new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 55m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "EquipmentProducts",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000010"));

            migrationBuilder.DeleteData(
                table: "EquipmentProducts",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000011"));

            migrationBuilder.DeleteData(
                table: "EquipmentProducts",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                table: "EquipmentProducts",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                table: "EquipmentProducts",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000014"));

            migrationBuilder.DeleteData(
                table: "EquipmentProducts",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000015"));

            migrationBuilder.DeleteData(
                table: "EquipmentProducts",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000016"));
        }
    }
}
