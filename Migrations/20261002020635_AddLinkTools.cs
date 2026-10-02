using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "qr_code",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: false),
                    Size = table.Column<int>(type: "integer", nullable: false),
                    Foreground = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Background = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    HasLogo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qr_code", x => x.Id);
                    table.ForeignKey(
                        name: "FK_qr_code_app_user_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tracked_link",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CampaignId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Medium = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Content = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Url = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracked_link", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tracked_link_app_user_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_qr_code_CreatedBy",
                schema: "panwar_portals",
                table: "qr_code",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_tracked_link_CampaignId",
                schema: "panwar_portals",
                table: "tracked_link",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_tracked_link_CreatedBy",
                schema: "panwar_portals",
                table: "tracked_link",
                column: "CreatedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "qr_code",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "tracked_link",
                schema: "panwar_portals");
        }
    }
}
