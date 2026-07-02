using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportAiLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_ai_log",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    TranscriptJson = table.Column<string>(type: "jsonb", nullable: false),
                    AnswerJson = table.Column<string>(type: "jsonb", nullable: true),
                    VerificationJson = table.Column<string>(type: "jsonb", nullable: false),
                    CellsReadJson = table.Column<string>(type: "jsonb", nullable: false),
                    GroundingJson = table.Column<string>(type: "jsonb", nullable: false),
                    OutcomeJson = table.Column<string>(type: "jsonb", nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    ToolCallCount = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_ai_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_ai_log_ClientId_ContentHash",
                schema: "panwar_portals",
                table: "import_ai_log",
                columns: new[] { "ClientId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_ai_log",
                schema: "panwar_portals");
        }
    }
}
