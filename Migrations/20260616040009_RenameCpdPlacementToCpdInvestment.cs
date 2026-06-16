using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameCpdPlacementToCpdInvestment : Migration
    {
        // Rename (NOT drop+create) so the seeded rows are preserved.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "cpd_placement",
                schema: "panwar_portals",
                newName: "cpd_investment",
                newSchema: "panwar_portals");

            migrationBuilder.RenameIndex(name: "IX_cpd_placement_AudienceId", newName: "IX_cpd_investment_AudienceId", table: "cpd_investment", schema: "panwar_portals");
            migrationBuilder.RenameIndex(name: "IX_cpd_placement_BrandId", newName: "IX_cpd_investment_BrandId", table: "cpd_investment", schema: "panwar_portals");
            migrationBuilder.RenameIndex(name: "IX_cpd_placement_BrandId_AudienceId_Year", newName: "IX_cpd_investment_BrandId_AudienceId_Year", table: "cpd_investment", schema: "panwar_portals");
            migrationBuilder.RenameIndex(name: "IX_cpd_placement_PublisherId", newName: "IX_cpd_investment_PublisherId", table: "cpd_investment", schema: "panwar_portals");

            migrationBuilder.Sql(@"ALTER TABLE panwar_portals.cpd_investment RENAME CONSTRAINT ""PK_cpd_placement"" TO ""PK_cpd_investment"";");
            migrationBuilder.Sql(@"ALTER TABLE panwar_portals.cpd_investment RENAME CONSTRAINT ""FK_cpd_placement_audience_AudienceId"" TO ""FK_cpd_investment_audience_AudienceId"";");
            migrationBuilder.Sql(@"ALTER TABLE panwar_portals.cpd_investment RENAME CONSTRAINT ""FK_cpd_placement_brand_BrandId"" TO ""FK_cpd_investment_brand_BrandId"";");
            migrationBuilder.Sql(@"ALTER TABLE panwar_portals.cpd_investment RENAME CONSTRAINT ""FK_cpd_placement_publisher_PublisherId"" TO ""FK_cpd_investment_publisher_PublisherId"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE panwar_portals.cpd_investment RENAME CONSTRAINT ""PK_cpd_investment"" TO ""PK_cpd_placement"";");
            migrationBuilder.Sql(@"ALTER TABLE panwar_portals.cpd_investment RENAME CONSTRAINT ""FK_cpd_investment_audience_AudienceId"" TO ""FK_cpd_placement_audience_AudienceId"";");
            migrationBuilder.Sql(@"ALTER TABLE panwar_portals.cpd_investment RENAME CONSTRAINT ""FK_cpd_investment_brand_BrandId"" TO ""FK_cpd_placement_brand_BrandId"";");
            migrationBuilder.Sql(@"ALTER TABLE panwar_portals.cpd_investment RENAME CONSTRAINT ""FK_cpd_investment_publisher_PublisherId"" TO ""FK_cpd_placement_publisher_PublisherId"";");

            migrationBuilder.RenameIndex(name: "IX_cpd_investment_AudienceId", newName: "IX_cpd_placement_AudienceId", table: "cpd_placement", schema: "panwar_portals");
            migrationBuilder.RenameIndex(name: "IX_cpd_investment_BrandId", newName: "IX_cpd_placement_BrandId", table: "cpd_placement", schema: "panwar_portals");
            migrationBuilder.RenameIndex(name: "IX_cpd_investment_BrandId_AudienceId_Year", newName: "IX_cpd_placement_BrandId_AudienceId_Year", table: "cpd_placement", schema: "panwar_portals");
            migrationBuilder.RenameIndex(name: "IX_cpd_investment_PublisherId", newName: "IX_cpd_placement_PublisherId", table: "cpd_placement", schema: "panwar_portals");

            migrationBuilder.RenameTable(
                name: "cpd_investment",
                schema: "panwar_portals",
                newName: "cpd_placement",
                newSchema: "panwar_portals");
        }
    }
}
