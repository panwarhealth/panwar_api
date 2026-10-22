using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTsActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ts_activity",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    HoursBefore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    Hours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_activity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ts_activity_ts_job_FromJobId",
                        column: x => x.FromJobId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ts_activity_ts_job_JobId",
                        column: x => x.JobId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ts_activity_ts_person_PerformedById",
                        column: x => x.PerformedById,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ts_activity_ts_person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ts_activity_ts_task_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ts_activity_At",
                schema: "panwar_portals",
                table: "ts_activity",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_ts_activity_FromJobId",
                schema: "panwar_portals",
                table: "ts_activity",
                column: "FromJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_activity_JobId",
                schema: "panwar_portals",
                table: "ts_activity",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_activity_PerformedById",
                schema: "panwar_portals",
                table: "ts_activity",
                column: "PerformedById");

            migrationBuilder.CreateIndex(
                name: "IX_ts_activity_PersonId",
                schema: "panwar_portals",
                table: "ts_activity",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_activity_TaskId",
                schema: "panwar_portals",
                table: "ts_activity",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ts_activity",
                schema: "panwar_portals");
        }
    }
}
