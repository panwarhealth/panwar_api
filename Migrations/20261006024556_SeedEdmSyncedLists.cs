using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <summary>
    /// The two synced lists exist from the start, one per platform, so staff never set them up.
    /// They have no human creator, hence CreatedBy becoming nullable. EdmSyncService fills them.
    /// </summary>
    public partial class SeedEdmSyncedLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedBy",
                schema: "panwar_portals",
                table: "edm_list",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Kind 1 = Synced; SyncSource 0 = PharmaChat, 1 = ClinicalStudio. Senders from SeedEdmSenders.
            migrationBuilder.Sql("""
                INSERT INTO panwar_portals.edm_list ("Id", "Name", "Kind", "SyncSource", "SenderId")
                SELECT v.id::uuid, v.name, 1, v.source, s."Id"
                FROM (VALUES
                  ('a1c7e3f9-4b2d-4e8a-9f60-1d3b5c7e9a44', 'PharmaChat listeners', 0, 'updates@pharmachat.com.au'),
                  ('c5e9b1d3-7f4a-4c2e-8b91-3f5d7a9c1e55', 'Clinical Studio learners', 1, 'insights@clinicalstudio.com.au')
                ) AS v(id, name, source, sender)
                JOIN panwar_portals.edm_sender s ON s."FromAddress" = v.sender
                ON CONFLICT ("SyncSource") WHERE "SyncSource" IS NOT NULL DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM panwar_portals.edm_list l
                WHERE l."Id" IN ('a1c7e3f9-4b2d-4e8a-9f60-1d3b5c7e9a44', 'c5e9b1d3-7f4a-4c2e-8b91-3f5d7a9c1e55')
                  AND NOT EXISTS (SELECT 1 FROM panwar_portals.edm_campaign c WHERE c."ListId" = l."Id");
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedBy",
                schema: "panwar_portals",
                table: "edm_list",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
