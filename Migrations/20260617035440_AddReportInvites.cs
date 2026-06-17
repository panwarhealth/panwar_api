using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReportInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_invite",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Template = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    StartMonth = table.Column<int>(type: "integer", nullable: true),
                    EndMonth = table.Column<int>(type: "integer", nullable: true),
                    Token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SendCount = table.Column<int>(type: "integer", nullable: false),
                    ClickedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClickCount = table.Column<int>(type: "integer", nullable: false),
                    ViewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_invite", x => x.Id);
                    table.ForeignKey(
                        name: "FK_report_invite_app_user_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_report_invite_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invite_event",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InviteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsBot = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invite_event", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invite_event_report_invite_InviteId",
                        column: x => x.InviteId,
                        principalSchema: "panwar_portals",
                        principalTable: "report_invite",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invite_event_InviteId_At",
                schema: "panwar_portals",
                table: "invite_event",
                columns: new[] { "InviteId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_report_invite_ClientId_RecipientUserId_Template_Year",
                schema: "panwar_portals",
                table: "report_invite",
                columns: new[] { "ClientId", "RecipientUserId", "Template", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_report_invite_RecipientUserId",
                schema: "panwar_portals",
                table: "report_invite",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_report_invite_Token",
                schema: "panwar_portals",
                table: "report_invite",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invite_event",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "report_invite",
                schema: "panwar_portals");
        }
    }
}
