using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClientSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_client_Slug",
                schema: "panwar_portals",
                table: "client");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                schema: "panwar_portals",
                table: "client",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_Slug",
                schema: "panwar_portals",
                table: "client",
                column: "Slug",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            // Hard-delete a client and all its data, children-first (the Restrict FKs on
            // placement/cpd/education_course block a naive DELETE client). Called by the
            // daily PurgeDeletedClients timer for clients soft-deleted 30+ days ago.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION panwar_portals.purge_client(p_client uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    DELETE FROM panwar_portals.placement_actual  WHERE ""PlacementId"" IN (
        SELECT p.""Id"" FROM panwar_portals.placement p
        JOIN panwar_portals.brand b ON p.""BrandId"" = b.""Id"" WHERE b.""ClientId"" = p_client);
    DELETE FROM panwar_portals.placement_kpi     WHERE ""PlacementId"" IN (
        SELECT p.""Id"" FROM panwar_portals.placement p
        JOIN panwar_portals.brand b ON p.""BrandId"" = b.""Id"" WHERE b.""ClientId"" = p_client);
    DELETE FROM panwar_portals.placement_comment WHERE ""PlacementId"" IN (
        SELECT p.""Id"" FROM panwar_portals.placement p
        JOIN panwar_portals.brand b ON p.""BrandId"" = b.""Id"" WHERE b.""ClientId"" = p_client);
    DELETE FROM panwar_portals.placement WHERE ""BrandId"" IN (
        SELECT ""Id"" FROM panwar_portals.brand WHERE ""ClientId"" = p_client);

    DELETE FROM panwar_portals.cpd_investment WHERE ""BrandId"" IN (
        SELECT ""Id"" FROM panwar_portals.brand WHERE ""ClientId"" = p_client);

    DELETE FROM panwar_portals.education_course_status WHERE ""CourseId"" IN (
        SELECT c.""Id"" FROM panwar_portals.education_course c
        JOIN panwar_portals.brand b ON c.""BrandId"" = b.""Id"" WHERE b.""ClientId"" = p_client);
    DELETE FROM panwar_portals.education_course WHERE ""BrandId"" IN (
        SELECT ""Id"" FROM panwar_portals.brand WHERE ""ClientId"" = p_client);

    DELETE FROM panwar_portals.client WHERE ""Id"" = p_client;
END;
$$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS panwar_portals.purge_client(uuid);");
            migrationBuilder.DropIndex(
                name: "IX_client_Slug",
                schema: "panwar_portals",
                table: "client");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "panwar_portals",
                table: "client");

            migrationBuilder.CreateIndex(
                name: "IX_client_Slug",
                schema: "panwar_portals",
                table: "client",
                column: "Slug",
                unique: true);
        }
    }
}
