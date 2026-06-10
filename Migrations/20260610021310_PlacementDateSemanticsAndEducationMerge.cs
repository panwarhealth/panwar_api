using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class PlacementDateSemanticsAndEducationMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EdmSubcategory",
                schema: "panwar_portals",
                table: "placement",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EducationSubcategory",
                schema: "panwar_portals",
                table: "placement",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                schema: "panwar_portals",
                table: "placement",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                schema: "panwar_portals",
                table: "placement",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                schema: "panwar_portals",
                table: "placement",
                type: "date",
                nullable: true);

            // ── Data migration ────────────────────────────────────────────────
            // Two education template rows existed: "Education Video" (Code 4, no
            // placements) and "Education Course" (Code 5, all the education
            // placements + KPIs). They merge into a single "Education" template.
            // Survivor = the Code 5 row (keeps its placements/KPIs/fields), which
            // is then re-coded to 4. Victim = the Code 5-less Code 4 row, deleted
            // (its metric_field + publisher_template rows cascade). All steps are
            // keyed on Code so they run identically on any environment.

            // 1. Education placements get a sub-category (existing data is all
            //    completion modules).
            migrationBuilder.Sql(@"
                UPDATE panwar_portals.placement p
                SET ""EducationSubcategory"" = 0
                WHERE p.""TemplateId"" IN (SELECT ""Id"" FROM panwar_portals.metric_template WHERE ""Code"" = 5);");

            // 2. Carry the victim template's publisher offerings onto the survivor
            //    before the cascade removes them.
            migrationBuilder.Sql(@"
                INSERT INTO panwar_portals.publisher_template (""PublisherId"", ""TemplateId"")
                SELECT pt.""PublisherId"", (SELECT ""Id"" FROM panwar_portals.metric_template WHERE ""Code"" = 5)
                FROM panwar_portals.publisher_template pt
                WHERE pt.""TemplateId"" = (SELECT ""Id"" FROM panwar_portals.metric_template WHERE ""Code"" = 4)
                  AND NOT EXISTS (
                      SELECT 1 FROM panwar_portals.publisher_template x
                      WHERE x.""PublisherId"" = pt.""PublisherId""
                        AND x.""TemplateId"" = (SELECT ""Id"" FROM panwar_portals.metric_template WHERE ""Code"" = 5));");

            // 3. Education placements become date-range driven. Existing rows have
            //    empty LiveMonths, so derive the range from their monthly actuals
            //    (falling back to the whole reporting year). LiveMonths is then
            //    cleared — the date range is authoritative for education.
            migrationBuilder.Sql(@"
                UPDATE panwar_portals.placement p
                SET ""StartDate"" = COALESCE(
                        (SELECT make_date(a.""Year"", a.""Month"", 1)
                           FROM panwar_portals.placement_actual a
                           WHERE a.""PlacementId"" = p.""Id""
                           ORDER BY a.""Year"", a.""Month"" LIMIT 1),
                        make_date(p.""Year"", 1, 1)),
                    ""EndDate"" = COALESCE(
                        (SELECT (make_date(a.""Year"", a.""Month"", 1) + INTERVAL '1 month' - INTERVAL '1 day')::date
                           FROM panwar_portals.placement_actual a
                           WHERE a.""PlacementId"" = p.""Id""
                           ORDER BY a.""Year"" DESC, a.""Month"" DESC LIMIT 1),
                        make_date(p.""Year"", 12, 31)),
                    ""LiveMonths"" = '{}'
                WHERE p.""TemplateId"" IN (SELECT ""Id"" FROM panwar_portals.metric_template WHERE ""Code"" = 5);");

            // 4. Legacy eDM placements get a sub-category guessed from the name
            //    (no send dates are fabricated — they keep their LiveMonths until
            //    an editor splits them into per-send rows via Duplicate).
            migrationBuilder.Sql(@"
                UPDATE panwar_portals.placement p
                SET ""EdmSubcategory"" = CASE
                        WHEN p.""Name"" ILIKE '%banner%' THEN 2
                        WHEN p.""Name"" ILIKE '%solus%'  THEN 0
                        ELSE 1 END
                WHERE p.""TemplateId"" IN (SELECT ""Id"" FROM panwar_portals.metric_template WHERE ""Code"" = 1);");

            // 5. Drop the victim template (cascades its metric_field +
            //    publisher_template), then re-code + rename the survivor.
            migrationBuilder.Sql(@"DELETE FROM panwar_portals.metric_template WHERE ""Code"" = 4;");
            migrationBuilder.Sql(@"UPDATE panwar_portals.metric_template SET ""Code"" = 4, ""Name"" = 'Education' WHERE ""Code"" = 5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EdmSubcategory",
                schema: "panwar_portals",
                table: "placement");

            migrationBuilder.DropColumn(
                name: "EducationSubcategory",
                schema: "panwar_portals",
                table: "placement");

            migrationBuilder.DropColumn(
                name: "EndDate",
                schema: "panwar_portals",
                table: "placement");

            migrationBuilder.DropColumn(
                name: "GroupId",
                schema: "panwar_portals",
                table: "placement");

            migrationBuilder.DropColumn(
                name: "StartDate",
                schema: "panwar_portals",
                table: "placement");
        }
    }
}
