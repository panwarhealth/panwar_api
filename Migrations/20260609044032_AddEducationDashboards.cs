using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationDashboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "education_page",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_education_page", x => x.Id);
                    table.ForeignKey(
                        name: "FK_education_page_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "education_chart",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationPageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_education_chart", x => x.Id);
                    table.ForeignKey(
                        name: "FK_education_chart_education_page_EducationPageId",
                        column: x => x.EducationPageId,
                        principalSchema: "panwar_portals",
                        principalTable: "education_page",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "education_series",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationChartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
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
                name: "education_annotation",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationChartId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_education_annotation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_education_annotation_app_user_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_education_annotation_education_chart_EducationChartId",
                        column: x => x.EducationChartId,
                        principalSchema: "panwar_portals",
                        principalTable: "education_chart",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_education_annotation_education_series_EducationSeriesId",
                        column: x => x.EducationSeriesId,
                        principalSchema: "panwar_portals",
                        principalTable: "education_series",
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
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
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
                name: "IX_education_annotation_CreatedByUserId",
                schema: "panwar_portals",
                table: "education_annotation",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_education_annotation_EducationChartId",
                schema: "panwar_portals",
                table: "education_annotation",
                column: "EducationChartId");

            migrationBuilder.CreateIndex(
                name: "IX_education_annotation_EducationSeriesId",
                schema: "panwar_portals",
                table: "education_annotation",
                column: "EducationSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_education_chart_EducationPageId",
                schema: "panwar_portals",
                table: "education_chart",
                column: "EducationPageId");

            migrationBuilder.CreateIndex(
                name: "IX_education_data_point_EducationSeriesId_Year_Month",
                schema: "panwar_portals",
                table: "education_data_point",
                columns: new[] { "EducationSeriesId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_education_page_ClientId_Slug",
                schema: "panwar_portals",
                table: "education_page",
                columns: new[] { "ClientId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_education_series_EducationChartId",
                schema: "panwar_portals",
                table: "education_series",
                column: "EducationChartId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "education_annotation",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "education_data_point",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "education_series",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "education_chart",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "education_page",
                schema: "panwar_portals");
        }
    }
}
