using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseVersionAndPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "course_presence",
                schema: "panwar_portals",
                columns: table => new
                {
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_presence", x => new { x.CourseId, x.UserId });
                    table.ForeignKey(
                        name: "FK_course_presence_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_course_presence_course_CourseId",
                        column: x => x.CourseId,
                        principalSchema: "panwar_portals",
                        principalTable: "course",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "course_version",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    SavedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_course_version_app_user_SavedBy",
                        column: x => x.SavedBy,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_course_version_course_CourseId",
                        column: x => x.CourseId,
                        principalSchema: "panwar_portals",
                        principalTable: "course",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_course_presence_UserId",
                schema: "panwar_portals",
                table: "course_presence",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_course_version_CourseId_SavedAt",
                schema: "panwar_portals",
                table: "course_version",
                columns: new[] { "CourseId", "SavedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_course_version_SavedBy",
                schema: "panwar_portals",
                table: "course_version",
                column: "SavedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "course_presence",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "course_version",
                schema: "panwar_portals");
        }
    }
}
