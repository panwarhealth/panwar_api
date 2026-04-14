using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserClientJunction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_client",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_client", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_client_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_client_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_client_ClientId",
                schema: "panwar_portals",
                table: "user_client",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_user_client_UserId_ClientId",
                schema: "panwar_portals",
                table: "user_client",
                columns: new[] { "UserId", "ClientId" },
                unique: true);

            // Data migration: move existing app_user.ClientId rows into user_client
            // before dropping the column.
            migrationBuilder.Sql(@"
                INSERT INTO panwar_portals.user_client (""Id"", ""UserId"", ""ClientId"", ""CreatedAt"")
                SELECT gen_random_uuid(), ""Id"", ""ClientId"", CURRENT_TIMESTAMP
                FROM panwar_portals.app_user
                WHERE ""ClientId"" IS NOT NULL;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_app_user_client_ClientId",
                schema: "panwar_portals",
                table: "app_user");

            migrationBuilder.DropIndex(
                name: "IX_app_user_ClientId",
                schema: "panwar_portals",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "ClientId",
                schema: "panwar_portals",
                table: "app_user");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_client",
                schema: "panwar_portals");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                schema: "panwar_portals",
                table: "app_user",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_user_ClientId",
                schema: "panwar_portals",
                table: "app_user",
                column: "ClientId");

            migrationBuilder.AddForeignKey(
                name: "FK_app_user_client_ClientId",
                schema: "panwar_portals",
                table: "app_user",
                column: "ClientId",
                principalSchema: "panwar_portals",
                principalTable: "client",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
