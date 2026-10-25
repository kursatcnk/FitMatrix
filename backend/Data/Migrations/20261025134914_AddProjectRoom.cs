using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitMatrix.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RoomDepthCm",
                table: "DeskProjects",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RoomWallGapCm",
                table: "DeskProjects",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RoomWidthCm",
                table: "DeskProjects",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoomDepthCm",
                table: "DeskProjects");

            migrationBuilder.DropColumn(
                name: "RoomWallGapCm",
                table: "DeskProjects");

            migrationBuilder.DropColumn(
                name: "RoomWidthCm",
                table: "DeskProjects");
        }
    }
}
