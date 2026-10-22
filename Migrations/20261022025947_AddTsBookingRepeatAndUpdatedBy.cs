using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTsBookingRepeatAndUpdatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RepeatSetId",
                schema: "panwar_portals",
                table: "ts_booking",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                schema: "panwar_portals",
                table: "ts_booking",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ts_booking_RepeatSetId",
                schema: "panwar_portals",
                table: "ts_booking",
                column: "RepeatSetId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_booking_UpdatedById",
                schema: "panwar_portals",
                table: "ts_booking",
                column: "UpdatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_ts_booking_ts_person_UpdatedById",
                schema: "panwar_portals",
                table: "ts_booking",
                column: "UpdatedById",
                principalSchema: "panwar_portals",
                principalTable: "ts_person",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ts_booking_ts_person_UpdatedById",
                schema: "panwar_portals",
                table: "ts_booking");

            migrationBuilder.DropIndex(
                name: "IX_ts_booking_RepeatSetId",
                schema: "panwar_portals",
                table: "ts_booking");

            migrationBuilder.DropIndex(
                name: "IX_ts_booking_UpdatedById",
                schema: "panwar_portals",
                table: "ts_booking");

            migrationBuilder.DropColumn(
                name: "RepeatSetId",
                schema: "panwar_portals",
                table: "ts_booking");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                schema: "panwar_portals",
                table: "ts_booking");
        }
    }
}
