using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitMatrix.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefineEditorModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000006"),
                column: "EditorModel",
                value: "camera");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000007"),
                column: "EditorModel",
                value: "router");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000008"),
                column: "EditorModel",
                value: "console");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000009"),
                column: "EditorModel",
                value: "phone");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000010"),
                column: "EditorModel",
                value: "desk");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000012"),
                column: "EditorModel",
                value: "power");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000013"),
                column: "EditorModel",
                value: "printer");

            migrationBuilder.Sql("""
                UPDATE product
                SET product.EditorModel = CASE category.Slug
                    WHEN 'imaging' THEN 'camera'
                    WHEN 'network' THEN 'router'
                    WHEN 'gaming' THEN 'console'
                    WHEN 'mobile' THEN 'phone'
                    WHEN 'furniture' THEN 'desk'
                    WHEN 'power' THEN 'power'
                    WHEN 'office' THEN 'printer'
                END
                FROM EquipmentProducts AS product
                INNER JOIN EquipmentCategories AS category ON category.Id = product.CategoryId
                WHERE product.EditorModel = 'generic'
                  AND category.Slug IN ('imaging', 'network', 'gaming', 'mobile', 'furniture', 'power', 'office');

                UPDATE DeskProjectItems
                SET EditorModel = CASE CategorySlug
                    WHEN 'imaging' THEN 'camera'
                    WHEN 'network' THEN 'router'
                    WHEN 'gaming' THEN 'console'
                    WHEN 'mobile' THEN 'phone'
                    WHEN 'furniture' THEN 'desk'
                    WHEN 'power' THEN 'power'
                    WHEN 'office' THEN 'printer'
                END
                WHERE EditorModel = 'generic'
                  AND CategorySlug IN ('imaging', 'network', 'gaming', 'mobile', 'furniture', 'power', 'office');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE product
                SET product.EditorModel = 'generic'
                FROM EquipmentProducts AS product
                INNER JOIN EquipmentCategories AS category ON category.Id = product.CategoryId
                WHERE (category.Slug = 'imaging' AND product.EditorModel = 'camera')
                   OR (category.Slug = 'network' AND product.EditorModel = 'router')
                   OR (category.Slug = 'gaming' AND product.EditorModel = 'console')
                   OR (category.Slug = 'mobile' AND product.EditorModel = 'phone')
                   OR (category.Slug = 'furniture' AND product.EditorModel = 'desk')
                   OR (category.Slug = 'power' AND product.EditorModel = 'power')
                   OR (category.Slug = 'office' AND product.EditorModel = 'printer');

                UPDATE DeskProjectItems
                SET EditorModel = 'generic'
                WHERE (CategorySlug = 'imaging' AND EditorModel = 'camera')
                   OR (CategorySlug = 'network' AND EditorModel = 'router')
                   OR (CategorySlug = 'gaming' AND EditorModel = 'console')
                   OR (CategorySlug = 'mobile' AND EditorModel = 'phone')
                   OR (CategorySlug = 'furniture' AND EditorModel = 'desk')
                   OR (CategorySlug = 'power' AND EditorModel = 'power')
                   OR (CategorySlug = 'office' AND EditorModel = 'printer');
                """);

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000006"),
                column: "EditorModel",
                value: "generic");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000007"),
                column: "EditorModel",
                value: "generic");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000008"),
                column: "EditorModel",
                value: "generic");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000009"),
                column: "EditorModel",
                value: "generic");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000010"),
                column: "EditorModel",
                value: "generic");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000012"),
                column: "EditorModel",
                value: "generic");

            migrationBuilder.UpdateData(
                table: "EquipmentCategories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000013"),
                column: "EditorModel",
                value: "generic");
        }
    }
}
