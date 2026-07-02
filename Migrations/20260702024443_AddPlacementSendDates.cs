using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlacementSendDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly[]>(
                name: "SendDates",
                schema: "panwar_portals",
                table: "placement",
                type: "date[]",
                nullable: false,
                defaultValue: new DateOnly[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SendDates",
                schema: "panwar_portals",
                table: "placement");
        }
    }
}
