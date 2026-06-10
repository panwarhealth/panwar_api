using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClientOverviewChartToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowBrandMonthlyChart",
                schema: "panwar_portals",
                table: "client",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowPublisherChart",
                schema: "panwar_portals",
                table: "client",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowBrandMonthlyChart",
                schema: "panwar_portals",
                table: "client");

            migrationBuilder.DropColumn(
                name: "ShowPublisherChart",
                schema: "panwar_portals",
                table: "client");
        }
    }
}
