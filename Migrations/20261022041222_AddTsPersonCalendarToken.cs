using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTsPersonCalendarToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalendarToken",
                schema: "panwar_portals",
                table: "ts_person",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ts_person_CalendarToken",
                schema: "panwar_portals",
                table: "ts_person",
                column: "CalendarToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ts_person_CalendarToken",
                schema: "panwar_portals",
                table: "ts_person");

            migrationBuilder.DropColumn(
                name: "CalendarToken",
                schema: "panwar_portals",
                table: "ts_person");
        }
    }
}
