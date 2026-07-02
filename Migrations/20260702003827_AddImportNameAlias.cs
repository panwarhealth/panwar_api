using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportNameAlias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_name_alias",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PlacementId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_name_alias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_import_name_alias_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_import_name_alias_placement_PlacementId",
                        column: x => x.PlacementId,
                        principalSchema: "panwar_portals",
                        principalTable: "placement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_name_alias_ClientId_SourceName",
                schema: "panwar_portals",
                table: "import_name_alias",
                columns: new[] { "ClientId", "SourceName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_import_name_alias_PlacementId",
                schema: "panwar_portals",
                table: "import_name_alias",
                column: "PlacementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_name_alias",
                schema: "panwar_portals");
        }
    }
}
