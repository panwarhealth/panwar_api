using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class YearTargetsAndSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_client_publisher_baseline_ClientId_PublisherId_TemplateId_M~",
                schema: "panwar_portals",
                table: "client_publisher_baseline");

            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                schema: "panwar_portals",
                table: "client_publisher_baseline");

            migrationBuilder.DropColumn(
                name: "EffectiveTo",
                schema: "panwar_portals",
                table: "client_publisher_baseline");

            migrationBuilder.AddColumn<int>(
                name: "Year",
                schema: "panwar_portals",
                table: "client_publisher_baseline",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "client_year_summary",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_year_summary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_year_summary_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_publisher_baseline_ClientId_PublisherId_TemplateId_M~",
                schema: "panwar_portals",
                table: "client_publisher_baseline",
                columns: new[] { "ClientId", "PublisherId", "TemplateId", "MetricKey", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_year_summary_ClientId_Year",
                schema: "panwar_portals",
                table: "client_year_summary",
                columns: new[] { "ClientId", "Year" },
                unique: true);

            // ── 2026 collection-request metric fields ─────────────────────────
            // Fields the 2026 Media Data Collection Requests workbook asks for
            // that the templates don't carry yet. Unique variants are reported
            // per placement only (never rolled into touchpoint/engagement sums,
            // which would double-count). Keyed on template Code; idempotent via
            // the (TemplateId, Key) unique index guard.
            migrationBuilder.Sql(@"
                INSERT INTO panwar_portals.metric_field (""Id"", ""TemplateId"", ""Key"", ""Label"", ""Unit"", ""IsCalculated"", ""CalcFormula"", ""SortOrder"")
                SELECT gen_random_uuid(), t.""Id"", v.key, v.label, NULL, FALSE, NULL, v.sort
                FROM panwar_portals.metric_template t
                CROSS JOIN (VALUES
                    (1, 'unique_opens',     'Unique Opens',     10),
                    (1, 'unique_clicks',    'Unique Clicks',    11),
                    (1, 'downloads',        'Downloads',        12),
                    (1, 'unique_downloads', 'Unique Downloads', 13),
                    (0, 'unique_impressions', 'Unique Impressions', 10),
                    (0, 'unique_clicks',      'Unique Clicks',      11),
                    (4, 'unique_page_views', 'Unique Page Views', 10),
                    (4, 'downloads',         'Downloads',         11)
                ) AS v(code, key, label, sort)
                WHERE t.""Code"" = v.code
                  AND NOT EXISTS (
                      SELECT 1 FROM panwar_portals.metric_field f
                      WHERE f.""TemplateId"" = t.""Id"" AND f.""Key"" = v.key);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_year_summary",
                schema: "panwar_portals");

            migrationBuilder.DropIndex(
                name: "IX_client_publisher_baseline_ClientId_PublisherId_TemplateId_M~",
                schema: "panwar_portals",
                table: "client_publisher_baseline");

            migrationBuilder.DropColumn(
                name: "Year",
                schema: "panwar_portals",
                table: "client_publisher_baseline");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveFrom",
                schema: "panwar_portals",
                table: "client_publisher_baseline",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveTo",
                schema: "panwar_portals",
                table: "client_publisher_baseline",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_publisher_baseline_ClientId_PublisherId_TemplateId_M~",
                schema: "panwar_portals",
                table: "client_publisher_baseline",
                columns: new[] { "ClientId", "PublisherId", "TemplateId", "MetricKey", "EffectiveFrom" },
                unique: true);
        }
    }
}
