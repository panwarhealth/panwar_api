using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <summary>
    /// The three eDM senders, matching the sender usernames on the ACS domains
    /// (scripts/edm-azure-setup.sh). Adding a brand means adding it there and in a migration like this.
    /// Footers carry the sender identification the Spam Act requires.
    /// </summary>
    public partial class SeedEdmSenders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO panwar_portals.edm_sender ("Id", "Name", "FromAddress", "BrandColour", "FooterText")
                VALUES
                  ('6d1f3c2a-8e4b-4f0a-9c51-3a7e2b9d0e11', 'PharmaChat', 'updates@pharmachat.com.au', '#951b81',
                   'You''re getting this because you opted in to PharmaChat emails. PharmaChat is run by Panwar Health Pty Ltd (ABN 15 631 093 966), PO Box 3227, Wamberal NSW 2260, Australia.'),
                  ('b3a9e0d4-2c7f-4e6b-8a15-5d9c1f4e7a22', 'Clinical Studio', 'insights@clinicalstudio.com.au', '#2c5282',
                   'You''re getting this because you opted in to Clinical Studio emails. Clinical Studio Pty Ltd (ABN 97 696 955 902), PO Box 3227, Wamberal NSW 2260, Australia.'),
                  ('e8c4f1b7-5a3d-4c9e-b2f6-7e1a0d3c5b33', 'Panwar Health', 'newsletter@panwarhealth.com.au', '#702f8f',
                   'You''re getting this because you subscribed to Panwar Health updates. Panwar Health Pty Ltd (ABN 15 631 093 966), PO Box 3227, Wamberal NSW 2260, Australia.')
                ON CONFLICT ("FromAddress") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Leave senders that lists or campaigns already point at.
            migrationBuilder.Sql("""
                DELETE FROM panwar_portals.edm_sender s
                WHERE s."Id" IN ('6d1f3c2a-8e4b-4f0a-9c51-3a7e2b9d0e11', 'b3a9e0d4-2c7f-4e6b-8a15-5d9c1f4e7a22', 'e8c4f1b7-5a3d-4c9e-b2f6-7e1a0d3c5b33')
                  AND NOT EXISTS (SELECT 1 FROM panwar_portals.edm_list l WHERE l."SenderId" = s."Id")
                  AND NOT EXISTS (SELECT 1 FROM panwar_portals.edm_campaign c WHERE c."SenderId" = s."Id");
                """);
        }
    }
}
