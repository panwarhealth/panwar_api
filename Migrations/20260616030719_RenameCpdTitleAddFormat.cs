using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameCpdTitleAddFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ArticleTitle",
                schema: "panwar_portals",
                table: "cpd_placement",
                newName: "Title");

            // Existing rows are all articles (the only CPD format in the Reckitt 2025 workbook).
            migrationBuilder.AddColumn<string>(
                name: "Format",
                schema: "panwar_portals",
                table: "cpd_placement",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "article");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Format",
                schema: "panwar_portals",
                table: "cpd_placement");

            migrationBuilder.RenameColumn(
                name: "Title",
                schema: "panwar_portals",
                table: "cpd_placement",
                newName: "ArticleTitle");
        }
    }
}
