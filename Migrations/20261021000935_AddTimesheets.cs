using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTimesheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ts_client",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CodePrefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SuppliesOwnCodes = table.Column<bool>(type: "boolean", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    HarvestId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_client", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ts_person",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsContractor = table.Column<bool>(type: "boolean", nullable: false),
                    IsPlaceholder = table.Column<bool>(type: "boolean", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    DayHours = table.Column<decimal[]>(type: "numeric(5,2)[]", nullable: false),
                    HarvestId = table.Column<long>(type: "bigint", nullable: true),
                    ForecastId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_person", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ts_task",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsCommon = table.Column<bool>(type: "boolean", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    HarvestId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_task", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ts_job",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    IsLeave = table.Column<bool>(type: "boolean", nullable: false),
                    Everyone = table.Column<bool>(type: "boolean", nullable: false),
                    AlertPercent = table.Column<int>(type: "integer", nullable: true),
                    AlertSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TrelloCardUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HarvestId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_job", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ts_job_ts_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ts_capacity_change",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_capacity_change", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ts_capacity_change_ts_person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ts_saved_report",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Query = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_saved_report", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ts_saved_report_ts_person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ts_booking",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HoursPerDay = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ForecastId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_booking", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ts_booking_ts_job_JobId",
                        column: x => x.JobId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ts_booking_ts_person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ts_booking_ts_task_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ts_entry",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Hours = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    TimerStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HarvestId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_entry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ts_entry_ts_job_JobId",
                        column: x => x.JobId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ts_entry_ts_person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ts_entry_ts_task_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ts_job_person",
                schema: "panwar_portals",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsManager = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_job_person", x => new { x.JobId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_ts_job_person_ts_job_JobId",
                        column: x => x.JobId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ts_job_person_ts_person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ts_job_task",
                schema: "panwar_portals",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetHours = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_job_task", x => new { x.JobId, x.TaskId });
                    table.ForeignKey(
                        name: "FK_ts_job_task_ts_job_JobId",
                        column: x => x.JobId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ts_job_task_ts_task_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ts_star",
                schema: "panwar_portals",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ts_star", x => new { x.PersonId, x.JobId, x.TaskId });
                    table.ForeignKey(
                        name: "FK_ts_star_ts_job_JobId",
                        column: x => x.JobId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ts_star_ts_person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ts_star_ts_task_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "panwar_portals",
                        principalTable: "ts_task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ts_booking_ForecastId",
                schema: "panwar_portals",
                table: "ts_booking",
                column: "ForecastId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ts_booking_JobId",
                schema: "panwar_portals",
                table: "ts_booking",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_booking_PersonId",
                schema: "panwar_portals",
                table: "ts_booking",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_booking_StartDate_EndDate",
                schema: "panwar_portals",
                table: "ts_booking",
                columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ts_booking_TaskId",
                schema: "panwar_portals",
                table: "ts_booking",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_capacity_change_PersonId_Date",
                schema: "panwar_portals",
                table: "ts_capacity_change",
                columns: new[] { "PersonId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ts_client_HarvestId",
                schema: "panwar_portals",
                table: "ts_client",
                column: "HarvestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ts_entry_Date",
                schema: "panwar_portals",
                table: "ts_entry",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ts_entry_HarvestId",
                schema: "panwar_portals",
                table: "ts_entry",
                column: "HarvestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ts_entry_JobId_Date",
                schema: "panwar_portals",
                table: "ts_entry",
                columns: new[] { "JobId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ts_entry_PersonId_Date",
                schema: "panwar_portals",
                table: "ts_entry",
                columns: new[] { "PersonId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ts_entry_TaskId",
                schema: "panwar_portals",
                table: "ts_entry",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_job_ClientId",
                schema: "panwar_portals",
                table: "ts_job",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_job_Code",
                schema: "panwar_portals",
                table: "ts_job",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_ts_job_HarvestId",
                schema: "panwar_portals",
                table: "ts_job",
                column: "HarvestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ts_job_person_PersonId",
                schema: "panwar_portals",
                table: "ts_job_person",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_job_task_TaskId",
                schema: "panwar_portals",
                table: "ts_job_task",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_person_Email",
                schema: "panwar_portals",
                table: "ts_person",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ts_person_ForecastId",
                schema: "panwar_portals",
                table: "ts_person",
                column: "ForecastId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ts_person_HarvestId",
                schema: "panwar_portals",
                table: "ts_person",
                column: "HarvestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ts_saved_report_PersonId",
                schema: "panwar_portals",
                table: "ts_saved_report",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_star_JobId",
                schema: "panwar_portals",
                table: "ts_star",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_star_TaskId",
                schema: "panwar_portals",
                table: "ts_star",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ts_task_HarvestId",
                schema: "panwar_portals",
                table: "ts_task",
                column: "HarvestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ts_booking",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "ts_capacity_change",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "ts_entry",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "ts_job_person",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "ts_job_task",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "ts_saved_report",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "ts_star",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "ts_job",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "ts_person",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "ts_task",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "ts_client",
                schema: "panwar_portals");
        }
    }
}
