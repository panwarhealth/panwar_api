using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlacementYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing placements to 2025 (all current data is calendar-2025).
            migrationBuilder.AddColumn<int>(
                name: "Year",
                schema: "panwar_portals",
                table: "placement",
                type: "integer",
                nullable: false,
                defaultValue: 2025);

            migrationBuilder.CreateIndex(
                name: "IX_placement_BrandId_AudienceId_Year",
                schema: "panwar_portals",
                table: "placement",
                columns: new[] { "BrandId", "AudienceId", "Year" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_placement_BrandId_AudienceId_Year",
                schema: "panwar_portals",
                table: "placement");

            migrationBuilder.DropColumn(
                name: "Year",
                schema: "panwar_portals",
                table: "placement");
        }
    }
}
