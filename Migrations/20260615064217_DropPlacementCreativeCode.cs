using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropPlacementCreativeCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreativeCode",
                schema: "panwar_portals",
                table: "placement");

            migrationBuilder.AlterColumn<string>(
                name: "OsCode",
                schema: "panwar_portals",
                table: "placement",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "OsCode",
                schema: "panwar_portals",
                table: "placement",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreativeCode",
                schema: "panwar_portals",
                table: "placement",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
