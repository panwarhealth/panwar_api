using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseBuildNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BuildNumber",
                schema: "panwar_portals",
                table: "course",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PublishedBuild",
                schema: "panwar_portals",
                table: "course",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublishedVersion",
                schema: "panwar_portals",
                table: "course",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuildNumber",
                schema: "panwar_portals",
                table: "course");

            migrationBuilder.DropColumn(
                name: "PublishedBuild",
                schema: "panwar_portals",
                table: "course");

            migrationBuilder.DropColumn(
                name: "PublishedVersion",
                schema: "panwar_portals",
                table: "course");
        }
    }
}
