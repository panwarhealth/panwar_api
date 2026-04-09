using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panwar.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "panwar_portals");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "client",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PrimaryColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AccentColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "magic_link",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_magic_link", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "metric_template",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metric_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "publisher",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publisher", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntraId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.Id);
                    table.ForeignKey(
                        name: "FK_app_user_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audience",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audience", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audience_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "brand",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brand", x => x.Id);
                    table.ForeignKey(
                        name: "FK_brand_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metric_field",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsCalculated = table.Column<bool>(type: "boolean", nullable: false),
                    CalcFormula = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metric_field", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metric_field_metric_template_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "panwar_portals",
                        principalTable: "metric_template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_publisher_baseline",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublisherId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_publisher_baseline", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_publisher_baseline_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_client_publisher_baseline_metric_template_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "panwar_portals",
                        principalTable: "metric_template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_publisher_baseline_publisher_PublisherId",
                        column: x => x.PublisherId,
                        principalSchema: "panwar_portals",
                        principalTable: "publisher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publisher_template",
                schema: "panwar_portals",
                columns: table => new
                {
                    PublisherId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publisher_template", x => new { x.PublisherId, x.TemplateId });
                    table.ForeignKey(
                        name: "FK_publisher_template_metric_template_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "panwar_portals",
                        principalTable: "metric_template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_publisher_template_publisher_PublisherId",
                        column: x => x.PublisherId,
                        principalSchema: "panwar_portals",
                        principalTable: "publisher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_role",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_role", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_role_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "education_course",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: false),
                    AudienceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublisherId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CourseType = table.Column<int>(type: "integer", nullable: false),
                    Presenter = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LaunchedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresAt = table.Column<DateOnly>(type: "date", nullable: true),
                    CpdInvestmentCost = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_education_course", x => x.Id);
                    table.ForeignKey(
                        name: "FK_education_course_audience_AudienceId",
                        column: x => x.AudienceId,
                        principalSchema: "panwar_portals",
                        principalTable: "audience",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_education_course_brand_BrandId",
                        column: x => x.BrandId,
                        principalSchema: "panwar_portals",
                        principalTable: "brand",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_education_course_publisher_PublisherId",
                        column: x => x.PublisherId,
                        principalSchema: "panwar_portals",
                        principalTable: "publisher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "month_snapshot",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: false),
                    AudienceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_month_snapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_month_snapshot_audience_AudienceId",
                        column: x => x.AudienceId,
                        principalSchema: "panwar_portals",
                        principalTable: "audience",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_month_snapshot_brand_BrandId",
                        column: x => x.BrandId,
                        principalSchema: "panwar_portals",
                        principalTable: "brand",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "education_course_status",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    CompleteCount = table.Column<int>(type: "integer", nullable: false),
                    PendingCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_education_course_status", x => x.Id);
                    table.ForeignKey(
                        name: "FK_education_course_status_education_course_CourseId",
                        column: x => x.CourseId,
                        principalSchema: "panwar_portals",
                        principalTable: "education_course",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "placement",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: false),
                    AudienceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublisherId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Objective = table.Column<int>(type: "integer", nullable: false),
                    AssetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreativeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OsCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UtmUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ArtworkUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Comments = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    LiveMonths = table.Column<int[]>(type: "integer[]", nullable: false),
                    MediaCost = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    CpdInvestmentCost = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    IsBonus = table.Column<bool>(type: "boolean", nullable: false),
                    IsCpdPackage = table.Column<bool>(type: "boolean", nullable: false),
                    Circulation = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    PlacementsCount = table.Column<int>(type: "integer", nullable: true),
                    TargetCourseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_placement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_placement_audience_AudienceId",
                        column: x => x.AudienceId,
                        principalSchema: "panwar_portals",
                        principalTable: "audience",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_placement_brand_BrandId",
                        column: x => x.BrandId,
                        principalSchema: "panwar_portals",
                        principalTable: "brand",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_placement_education_course_TargetCourseId",
                        column: x => x.TargetCourseId,
                        principalSchema: "panwar_portals",
                        principalTable: "education_course",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_placement_metric_template_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "panwar_portals",
                        principalTable: "metric_template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_placement_publisher_PublisherId",
                        column: x => x.PublisherId,
                        principalSchema: "panwar_portals",
                        principalTable: "publisher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "placement_actual",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlacementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    MetricKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_placement_actual", x => x.Id);
                    table.ForeignKey(
                        name: "FK_placement_actual_placement_PlacementId",
                        column: x => x.PlacementId,
                        principalSchema: "panwar_portals",
                        principalTable: "placement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "placement_comment",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlacementId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_placement_comment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_placement_comment_app_user_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "panwar_portals",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_placement_comment_placement_PlacementId",
                        column: x => x.PlacementId,
                        principalSchema: "panwar_portals",
                        principalTable: "placement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_placement_comment_placement_comment_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "panwar_portals",
                        principalTable: "placement_comment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "placement_kpi",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlacementId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetValue = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_placement_kpi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_placement_kpi_placement_PlacementId",
                        column: x => x.PlacementId,
                        principalSchema: "panwar_portals",
                        principalTable: "placement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "utm_link",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Destination = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreativeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OsCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    LiveMonths = table.Column<int[]>(type: "integer[]", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    PlacementId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_utm_link", x => x.Id);
                    table.ForeignKey(
                        name: "FK_utm_link_client_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "panwar_portals",
                        principalTable: "client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_utm_link_education_course_CourseId",
                        column: x => x.CourseId,
                        principalSchema: "panwar_portals",
                        principalTable: "education_course",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_utm_link_placement_PlacementId",
                        column: x => x.PlacementId,
                        principalSchema: "panwar_portals",
                        principalTable: "placement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "utm_link_clicks",
                schema: "panwar_portals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UtmLinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    ClickCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_utm_link_clicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_utm_link_clicks_utm_link_UtmLinkId",
                        column: x => x.UtmLinkId,
                        principalSchema: "panwar_portals",
                        principalTable: "utm_link",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_user_ClientId",
                schema: "panwar_portals",
                table: "app_user",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_Email",
                schema: "panwar_portals",
                table: "app_user",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_user_EntraId",
                schema: "panwar_portals",
                table: "app_user",
                column: "EntraId");

            migrationBuilder.CreateIndex(
                name: "IX_audience_ClientId_Slug",
                schema: "panwar_portals",
                table: "audience",
                columns: new[] { "ClientId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_EntityType_EntityId",
                schema: "panwar_portals",
                table: "audit_log",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_UserId",
                schema: "panwar_portals",
                table: "audit_log",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_brand_ClientId_Slug",
                schema: "panwar_portals",
                table: "brand",
                columns: new[] { "ClientId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_Slug",
                schema: "panwar_portals",
                table: "client",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_publisher_baseline_ClientId_PublisherId_TemplateId_M~",
                schema: "panwar_portals",
                table: "client_publisher_baseline",
                columns: new[] { "ClientId", "PublisherId", "TemplateId", "MetricKey", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_publisher_baseline_PublisherId",
                schema: "panwar_portals",
                table: "client_publisher_baseline",
                column: "PublisherId");

            migrationBuilder.CreateIndex(
                name: "IX_client_publisher_baseline_TemplateId",
                schema: "panwar_portals",
                table: "client_publisher_baseline",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_education_course_AudienceId",
                schema: "panwar_portals",
                table: "education_course",
                column: "AudienceId");

            migrationBuilder.CreateIndex(
                name: "IX_education_course_BrandId_AudienceId",
                schema: "panwar_portals",
                table: "education_course",
                columns: new[] { "BrandId", "AudienceId" });

            migrationBuilder.CreateIndex(
                name: "IX_education_course_PublisherId",
                schema: "panwar_portals",
                table: "education_course",
                column: "PublisherId");

            migrationBuilder.CreateIndex(
                name: "IX_education_course_status_CourseId_Year_Month",
                schema: "panwar_portals",
                table: "education_course_status",
                columns: new[] { "CourseId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_magic_link_Email",
                schema: "panwar_portals",
                table: "magic_link",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_magic_link_TokenHash",
                schema: "panwar_portals",
                table: "magic_link",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_metric_field_TemplateId_Key",
                schema: "panwar_portals",
                table: "metric_field",
                columns: new[] { "TemplateId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_metric_template_Code",
                schema: "panwar_portals",
                table: "metric_template",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_month_snapshot_AudienceId",
                schema: "panwar_portals",
                table: "month_snapshot",
                column: "AudienceId");

            migrationBuilder.CreateIndex(
                name: "IX_month_snapshot_BrandId_AudienceId_Year_Month",
                schema: "panwar_portals",
                table: "month_snapshot",
                columns: new[] { "BrandId", "AudienceId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_placement_AudienceId",
                schema: "panwar_portals",
                table: "placement",
                column: "AudienceId");

            migrationBuilder.CreateIndex(
                name: "IX_placement_BrandId",
                schema: "panwar_portals",
                table: "placement",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_placement_OsCode",
                schema: "panwar_portals",
                table: "placement",
                column: "OsCode");

            migrationBuilder.CreateIndex(
                name: "IX_placement_PublisherId",
                schema: "panwar_portals",
                table: "placement",
                column: "PublisherId");

            migrationBuilder.CreateIndex(
                name: "IX_placement_TargetCourseId",
                schema: "panwar_portals",
                table: "placement",
                column: "TargetCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_placement_TemplateId",
                schema: "panwar_portals",
                table: "placement",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_placement_actual_PlacementId_Year_Month_MetricKey",
                schema: "panwar_portals",
                table: "placement_actual",
                columns: new[] { "PlacementId", "Year", "Month", "MetricKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_placement_comment_AuthorUserId",
                schema: "panwar_portals",
                table: "placement_comment",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_placement_comment_ParentId",
                schema: "panwar_portals",
                table: "placement_comment",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_placement_comment_PlacementId",
                schema: "panwar_portals",
                table: "placement_comment",
                column: "PlacementId");

            migrationBuilder.CreateIndex(
                name: "IX_placement_kpi_PlacementId_MetricKey",
                schema: "panwar_portals",
                table: "placement_kpi",
                columns: new[] { "PlacementId", "MetricKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publisher_Slug",
                schema: "panwar_portals",
                table: "publisher",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publisher_template_TemplateId",
                schema: "panwar_portals",
                table: "publisher_template",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_UserId_Role",
                schema: "panwar_portals",
                table: "user_role",
                columns: new[] { "UserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_utm_link_ClientId",
                schema: "panwar_portals",
                table: "utm_link",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_utm_link_CourseId",
                schema: "panwar_portals",
                table: "utm_link",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_utm_link_OsCode",
                schema: "panwar_portals",
                table: "utm_link",
                column: "OsCode");

            migrationBuilder.CreateIndex(
                name: "IX_utm_link_PlacementId",
                schema: "panwar_portals",
                table: "utm_link",
                column: "PlacementId");

            migrationBuilder.CreateIndex(
                name: "IX_utm_link_clicks_UtmLinkId_Year_Month",
                schema: "panwar_portals",
                table: "utm_link_clicks",
                columns: new[] { "UtmLinkId", "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "client_publisher_baseline",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "education_course_status",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "magic_link",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "metric_field",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "month_snapshot",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "placement_actual",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "placement_comment",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "placement_kpi",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "publisher_template",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "user_role",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "utm_link_clicks",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "app_user",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "utm_link",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "placement",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "education_course",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "metric_template",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "audience",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "brand",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "publisher",
                schema: "panwar_portals");

            migrationBuilder.DropTable(
                name: "client",
                schema: "panwar_portals");
        }
    }
}
