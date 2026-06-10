using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "education_asset",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationPageId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Author = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_education_asset", x => x.Id);
                    table.ForeignKey(
                        name: "FK_education_asset_education_page_EducationPageId",
                        column: x => x.EducationPageId,
                        principalSchema: "panwar_portals",
                        principalTable: "education_page",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "education_asset_value",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_education_asset_value", x => x.Id);
                    table.ForeignKey(
                        name: "FK_education_asset_value_education_asset_EducationAssetId",
                        column: x => x.EducationAssetId,
                        principalSchema: "panwar_portals",
                        principalTable: "education_asset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_education_asset_EducationPageId",
                schema: "panwar_portals",
                table: "education_asset",
                column: "EducationPageId");

            migrationBuilder.CreateIndex(
                name: "IX_education_asset_value_EducationAssetId_Status_Year_Month",
                schema: "panwar_portals",
                table: "education_asset_value",
                columns: new[] { "EducationAssetId", "Status", "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "education_asset_value",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "education_asset",
                schema: "panwar_portals");
        }
    }
}
