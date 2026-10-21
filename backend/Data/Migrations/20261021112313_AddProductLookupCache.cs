using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitMatrix.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductLookupCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductLookupCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedQuery = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLookupCaches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductLookupCaches_NormalizedQuery",
                table: "ProductLookupCaches",
                column: "NormalizedQuery",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductLookupCaches");
        }
    }
}
