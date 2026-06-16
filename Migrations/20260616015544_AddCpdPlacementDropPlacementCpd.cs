using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCpdPlacementDropPlacementCpd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpdInvestmentCost",
                schema: "panwar_portals",
                table: "placement");

            migrationBuilder.DropColumn(
                name: "IsCpdPackage",
                schema: "panwar_portals",
                table: "placement");

            migrationBuilder.CreateTable(
                name: "cpd_placement",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: false),
                    AudienceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublisherId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    ArticleTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cpd_placement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cpd_placement_audience_AudienceId",
                        column: x => x.AudienceId,
                        principalSchema: "panwar_portals",
                        principalTable: "audience",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cpd_placement_brand_BrandId",
                        column: x => x.BrandId,
                        principalSchema: "panwar_portals",
                        principalTable: "brand",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cpd_placement_publisher_PublisherId",
                        column: x => x.PublisherId,
                        principalSchema: "panwar_portals",
                        principalTable: "publisher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cpd_placement_AudienceId",
                schema: "panwar_portals",
                table: "cpd_placement",
                column: "AudienceId");

            migrationBuilder.CreateIndex(
                name: "IX_cpd_placement_BrandId",
                schema: "panwar_portals",
                table: "cpd_placement",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_cpd_placement_BrandId_AudienceId_Year",
                schema: "panwar_portals",
                table: "cpd_placement",
                columns: new[] { "BrandId", "AudienceId", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_cpd_placement_PublisherId",
                schema: "panwar_portals",
                table: "cpd_placement",
                column: "PublisherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cpd_placement",
                schema: "panwar_portals");

            migrationBuilder.AddColumn<decimal>(
                name: "CpdInvestmentCost",
                schema: "panwar_portals",
                table: "placement",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCpdPackage",
                schema: "panwar_portals",
                table: "placement",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
