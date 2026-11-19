using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoursePublishedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                schema: "panwar_portals",
                table: "course",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedAt",
                schema: "panwar_portals",
                table: "course");
        }
    }
}
