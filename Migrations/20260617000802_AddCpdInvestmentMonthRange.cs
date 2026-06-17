using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCpdInvestmentMonthRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EndMonth",
                schema: "panwar_portals",
                table: "cpd_investment",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartMonth",
                schema: "panwar_portals",
                table: "cpd_investment",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndMonth",
                schema: "panwar_portals",
                table: "cpd_investment");

            migrationBuilder.DropColumn(
                name: "StartMonth",
                schema: "panwar_portals",
                table: "cpd_investment");
        }
    }
}
