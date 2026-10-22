using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTsBookingOnDaysOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OnDaysOff",
                schema: "panwar_portals",
                table: "ts_booking",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnDaysOff",
                schema: "panwar_portals",
                table: "ts_booking");
        }
    }
}
