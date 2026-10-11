using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitMatrix.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMountedDisplayLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ElevationCm",
                table: "DeskProjectItems",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "MountMode",
                table: "DeskProjectItems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "surface");

            migrationBuilder.AddColumn<string>(
                name: "ScreenOrientation",
                table: "DeskProjectItems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "landscape");

            migrationBuilder.AddColumn<decimal>(
                name: "TiltDegrees",
                table: "DeskProjectItems",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElevationCm",
                table: "DeskProjectItems");

            migrationBuilder.DropColumn(
                name: "MountMode",
                table: "DeskProjectItems");

            migrationBuilder.DropColumn(
                name: "ScreenOrientation",
                table: "DeskProjectItems");

            migrationBuilder.DropColumn(
                name: "TiltDegrees",
                table: "DeskProjectItems");
        }
    }
}
