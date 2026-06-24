using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_run",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FormatId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PlacementsWritten = table.Column<int>(type: "integer", nullable: false),
                    ValuesWritten = table.Column<int>(type: "integer", nullable: false),
                    ImportedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_run", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_run_ClientId_ContentHash",
                schema: "panwar_portals",
                table: "import_run",
                columns: new[] { "ClientId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_run",
                schema: "panwar_portals");
        }
    }
}
