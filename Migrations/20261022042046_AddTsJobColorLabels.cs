using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTsJobColorLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                schema: "panwar_portals",
                table: "ts_job",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ts_color_label",
                schema: "panwar_portals",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_color_label", x => x.Key);
                });

            migrationBuilder.InsertData(
                schema: "panwar_portals",
                table: "ts_color_label",
                columns: new[] { "Key", "Name", "SortOrder" },
                values: new object[,]
                {
                    { "aqua", "aqua", 6 },
                    { "black", "black", 9 },
                    { "blue", "blue", 5 },
                    { "gray", "gray", 8 },
                    { "green", "green", 7 },
                    { "magenta", "magenta", 3 },
                    { "orange", "orange", 2 },
                    { "purple", "purple", 4 },
                    { "red", "Reckitt", 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ts_color_label",
                schema: "panwar_portals");

            migrationBuilder.DropColumn(
                name: "Color",
                schema: "panwar_portals",
                table: "ts_job");
        }
    }
}
