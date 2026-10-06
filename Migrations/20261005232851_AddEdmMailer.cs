using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEdmMailer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edm_sender",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FromAddress = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    ReplyTo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    BrandColour = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FooterText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edm_sender", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "edm_list",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SyncSource = table.Column<int>(type: "integer", nullable: true),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edm_list", x => x.Id);
                    table.ForeignKey(
                        name: "FK_edm_list_app_user_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_edm_list_edm_sender_SenderId",
                        column: x => x.SenderId,
                        principalSchema: "panwar_portals",
                        principalTable: "edm_sender",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "edm_campaign",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CampaignCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ListId = table.Column<Guid>(type: "uuid", nullable: true),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Subject = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    PreviewText = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    HtmlBody = table.Column<string>(type: "text", nullable: true),
                    TextBody = table.Column<string>(type: "text", nullable: true),
                    SourceFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SentBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edm_campaign", x => x.Id);
                    table.ForeignKey(
                        name: "FK_edm_campaign_app_user_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_edm_campaign_app_user_SentBy",
                        column: x => x.SentBy,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_edm_campaign_edm_list_ListId",
                        column: x => x.ListId,
                        principalSchema: "panwar_portals",
                        principalTable: "edm_list",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_edm_campaign_edm_sender_SenderId",
                        column: x => x.SenderId,
                        principalSchema: "panwar_portals",
                        principalTable: "edm_sender",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "edm_contact",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WritebackPending = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edm_contact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_edm_contact_edm_list_ListId",
                        column: x => x.ListId,
                        principalSchema: "panwar_portals",
                        principalTable: "edm_list",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "edm_recipient",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstOpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenCount = table.Column<int>(type: "integer", nullable: false),
                    UnsubscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edm_recipient", x => x.Id);
                    table.ForeignKey(
                        name: "FK_edm_recipient_edm_campaign_CampaignId",
                        column: x => x.CampaignId,
                        principalSchema: "panwar_portals",
                        principalTable: "edm_campaign",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_edm_recipient_edm_contact_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "panwar_portals",
                        principalTable: "edm_contact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_edm_campaign_CreatedBy",
                schema: "panwar_portals",
                table: "edm_campaign",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_edm_campaign_ListId",
                schema: "panwar_portals",
                table: "edm_campaign",
                column: "ListId");

            migrationBuilder.CreateIndex(
                name: "IX_edm_campaign_SenderId",
                schema: "panwar_portals",
                table: "edm_campaign",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_edm_campaign_SentBy",
                schema: "panwar_portals",
                table: "edm_campaign",
                column: "SentBy");

            migrationBuilder.CreateIndex(
                name: "IX_edm_campaign_Status_ScheduledFor",
                schema: "panwar_portals",
                table: "edm_campaign",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_edm_contact_Email",
                schema: "panwar_portals",
                table: "edm_contact",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_edm_contact_ListId_Email",
                schema: "panwar_portals",
                table: "edm_contact",
                columns: new[] { "ListId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edm_list_CreatedBy",
                schema: "panwar_portals",
                table: "edm_list",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_edm_list_SenderId",
                schema: "panwar_portals",
                table: "edm_list",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_edm_list_SyncSource",
                schema: "panwar_portals",
                table: "edm_list",
                column: "SyncSource",
                unique: true,
                filter: "\"SyncSource\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_edm_recipient_CampaignId_Email",
                schema: "panwar_portals",
                table: "edm_recipient",
                columns: new[] { "CampaignId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edm_recipient_CampaignId_Status",
                schema: "panwar_portals",
                table: "edm_recipient",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_edm_recipient_ContactId",
                schema: "panwar_portals",
                table: "edm_recipient",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_edm_recipient_MessageId",
                schema: "panwar_portals",
                table: "edm_recipient",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_edm_sender_FromAddress",
                schema: "panwar_portals",
                table: "edm_sender",
                column: "FromAddress",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edm_recipient",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "edm_campaign",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "edm_contact",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "edm_list",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "edm_sender",
                schema: "panwar_portals");
        }
    }
}
