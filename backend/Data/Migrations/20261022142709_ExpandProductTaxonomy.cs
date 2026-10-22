using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FitMatrix.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandProductTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "EquipmentCategories",
                columns: new[] { "Id", "EditorModel", "Name", "ParentId", "Slug" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000006"), "generic", "Görüntü", null, "imaging" },
                    { new Guid("20000000-0000-0000-0000-000000000007"), "generic", "Ağ", null, "network" },
                    { new Guid("20000000-0000-0000-0000-000000000008"), "generic", "Oyun", null, "gaming" },
                    { new Guid("20000000-0000-0000-0000-000000000009"), "generic", "Mobil", null, "mobile" },
                    { new Guid("20000000-0000-0000-0000-000000000010"), "generic", "Mobilya", null, "furniture" },
                    { new Guid("20000000-0000-0000-0000-000000000011"), "lamp", "Aydınlatma", null, "lighting" },
                    { new Guid("20000000-0000-0000-0000-000000000012"), "generic", "Güç", null, "power" },
                    { new Guid("20000000-0000-0000-0000-000000000013"), "generic", "Ofis", null, "office" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000010"));

            migrationBuilder.DeleteData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000011"));

            migrationBuilder.DeleteData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000013"));
        }
    }
}
