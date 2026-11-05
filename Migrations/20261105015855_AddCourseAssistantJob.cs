using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseAssistantJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "course_assistant_job",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    Reply = table.Column<string>(type: "jsonb", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_assistant_job", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_course_assistant_job_CreatedAt",
                schema: "panwar_portals",
                table: "course_assistant_job",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "course_assistant_job",
                schema: "panwar_portals");
        }
    }
}
