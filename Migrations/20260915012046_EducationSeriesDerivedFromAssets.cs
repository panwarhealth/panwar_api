using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class EducationSeriesDerivedFromAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_education_annotation_education_series_EducationSeriesId",
                schema: "panwar_portals",
                table: "education_annotation");

            migrationBuilder.DropTable(
                name: "education_data_point",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "education_series",
                schema: "panwar_portals");

            migrationBuilder.DropIndex(
                name: "IX_education_annotation_EducationSeriesId",
                schema: "panwar_portals",
                table: "education_annotation");

            migrationBuilder.DropColumn(
                name: "EducationSeriesId",
                schema: "panwar_portals",
                table: "education_annotation");

            migrationBuilder.AddColumn<string[]>(
                name: "GroupLabels",
                schema: "panwar_portals",
                table: "education_chart",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "Brand",
                schema: "panwar_portals",
                table: "education_annotation",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupLabels",
                schema: "panwar_portals",
                table: "education_chart");

            migrationBuilder.DropColumn(
                name: "Brand",
                schema: "panwar_portals",
                table: "education_annotation");

            migrationBuilder.AddColumn<Guid>(
                name: "EducationSeriesId",
                schema: "panwar_portals",
                table: "education_annotation",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "education_series",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationChartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_education_series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_education_series_education_chart_EducationChartId",
                        column: x => x.EducationChartId,
                        principalSchema: "panwar_portals",
                        principalTable: "education_chart",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "education_data_point",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_education_data_point", x => x.Id);
                    table.ForeignKey(
                        name: "FK_education_data_point_education_series_EducationSeriesId",
                        column: x => x.EducationSeriesId,
                        principalSchema: "panwar_portals",
                        principalTable: "education_series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_education_annotation_EducationSeriesId",
                schema: "panwar_portals",
                table: "education_annotation",
                column: "EducationSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_education_data_point_EducationSeriesId_Year_Month",
                schema: "panwar_portals",
                table: "education_data_point",
                columns: new[] { "EducationSeriesId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_education_series_EducationChartId",
                schema: "panwar_portals",
                table: "education_series",
                column: "EducationChartId");

            migrationBuilder.AddForeignKey(
                name: "FK_education_annotation_education_series_EducationSeriesId",
                schema: "panwar_portals",
                table: "education_annotation",
                column: "EducationSeriesId",
                principalSchema: "panwar_portals",
                principalTable: "education_series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
