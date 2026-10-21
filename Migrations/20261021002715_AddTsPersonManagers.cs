using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTsPersonManagers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ts_person_manager",
                schema: "panwar_portals",
                columns: table => new
                {
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_person_manager", x => new { x.ManagerId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_ts_person_manager_ts_person_ManagerId",
                        column: x => x.ManagerId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ts_person_manager_ts_person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ts_person_manager_PersonId",
                schema: "panwar_portals",
                table: "ts_person_manager",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ts_person_manager",
                schema: "panwar_portals");
        }
    }
}
