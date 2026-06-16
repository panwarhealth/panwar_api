using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropPlacementAssetTypeAndUtmUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssetType",
                schema: "panwar_portals",
                table: "placement");

            migrationBuilder.DropColumn(
                name: "UtmUrl",
                schema: "panwar_portals",
                table: "placement");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetType",
                schema: "panwar_portals",
                table: "placement",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmUrl",
                schema: "panwar_portals",
                table: "placement",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }
    }
}
