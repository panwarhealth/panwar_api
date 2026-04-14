using Microsoft.EntityFrameworkCore;
using Panwar.Api.Models;

namespace Panwar.Api.Data;

public class AppDbContext : DbContext
{
    /// <summary>
    /// All Panwar Portals tables live in this dedicated Postgres schema,
    /// alongside (but isolated from) PharmaChat and Clinical Studio tables
    /// in the same shared `panwarhealth-db` database.
    /// </summary>
    public const string SchemaName = "panwar_portals";

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // Identity & tenancy
    public DbSet<Client> Clients { get; set; } = null!;
    public DbSet<Brand> Brands { get; set; } = null!;
    public DbSet<Audience> Audiences { get; set; } = null!;
    public DbSet<Publisher> Publishers { get; set; } = null!;

    // Metric templates
    public DbSet<MetricTemplate> MetricTemplates { get; set; } = null!;
    public DbSet<MetricField> MetricFields { get; set; } = null!;
    public DbSet<PublisherTemplate> PublisherTemplates { get; set; } = null!;

    // Baselines
    public DbSet<ClientPublisherBaseline> ClientPublisherBaselines { get; set; } = null!;

    // Placements
    public DbSet<Placement> Placements { get; set; } = null!;
    public DbSet<PlacementKpi> PlacementKpis { get; set; } = null!;
    public DbSet<PlacementActual> PlacementActuals { get; set; } = null!;
    public DbSet<PlacementComment> PlacementComments { get; set; } = null!;

    // Education
    public DbSet<EducationCourse> EducationCourses { get; set; } = null!;
    public DbSet<EducationCourseStatus> EducationCourseStatuses { get; set; } = null!;

    // UTM
    public DbSet<UtmLink> UtmLinks { get; set; } = null!;
    public DbSet<UtmLinkClicks> UtmLinkClicks { get; set; } = null!;

    // Workflow
    public DbSet<MonthSnapshot> MonthSnapshots { get; set; } = null!;

    // Users
    public DbSet<AppUser> Users { get; set; } = null!;
    public DbSet<UserClient> UserClients { get; set; } = null!;
    public DbSet<MagicLink> MagicLinks { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;

    // Audit
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);

        ConfigureClient(modelBuilder);
        ConfigureBrand(modelBuilder);
        ConfigureAudience(modelBuilder);
        ConfigurePublisher(modelBuilder);
        ConfigureMetricTemplate(modelBuilder);
        ConfigureMetricField(modelBuilder);
        ConfigurePublisherTemplate(modelBuilder);
        ConfigureClientPublisherBaseline(modelBuilder);
        ConfigurePlacement(modelBuilder);
        ConfigurePlacementKpi(modelBuilder);
        ConfigurePlacementActual(modelBuilder);
        ConfigurePlacementComment(modelBuilder);
        ConfigureEducationCourse(modelBuilder);
        ConfigureEducationCourseStatus(modelBuilder);
        ConfigureUtmLink(modelBuilder);
        ConfigureUtmLinkClicks(modelBuilder);
        ConfigureMonthSnapshot(modelBuilder);
        ConfigureAppUser(modelBuilder);
        ConfigureUserClient(modelBuilder);
        ConfigureMagicLink(modelBuilder);
        ConfigureUserRole(modelBuilder);
        ConfigureAuditLog(modelBuilder);
    }

    private static void ConfigureClient(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(entity =>
        {
            entity.ToTable("client");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LogoUrl).HasMaxLength(500);
            entity.Property(e => e.PrimaryColor).HasMaxLength(20);
            entity.Property(e => e.AccentColor).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.Slug).IsUnique();
        });
    }

    private static void ConfigureBrand(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>(entity =>
        {
            entity.ToTable("brand");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.Client).WithMany(c => c.Brands).HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ClientId, e.Slug }).IsUnique();
        });
    }

    private static void ConfigureAudience(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Audience>(entity =>
        {
            entity.ToTable("audience");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.Client).WithMany(c => c.Audiences).HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ClientId, e.Slug }).IsUnique();
        });
    }

    private static void ConfigurePublisher(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Publisher>(entity =>
        {
            entity.ToTable("publisher");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Website).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.Slug).IsUnique();
        });
    }

    private static void ConfigureMetricTemplate(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetricTemplate>(entity =>
        {
            entity.ToTable("metric_template");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).HasConversion<int>();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Code).IsUnique();
        });
    }

    private static void ConfigureMetricField(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetricField>(entity =>
        {
            entity.ToTable("metric_field");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Label).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Unit).HasMaxLength(20);
            entity.Property(e => e.CalcFormula).HasMaxLength(200);
            entity.HasOne(e => e.Template).WithMany(t => t.Fields).HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.TemplateId, e.Key }).IsUnique();
        });
    }

    private static void ConfigurePublisherTemplate(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PublisherTemplate>(entity =>
        {
            entity.ToTable("publisher_template");
            entity.HasKey(e => new { e.PublisherId, e.TemplateId });
            entity.HasOne(e => e.Publisher).WithMany(p => p.PublisherTemplates).HasForeignKey(e => e.PublisherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Template).WithMany(t => t.PublisherTemplates).HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureClientPublisherBaseline(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientPublisherBaseline>(entity =>
        {
            entity.ToTable("client_publisher_baseline");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MetricKey).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Value).HasColumnType("numeric(18,4)");
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.Client).WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Publisher).WithMany(p => p.ClientPublisherBaselines).HasForeignKey(e => e.PublisherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Template).WithMany().HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.ClientId, e.PublisherId, e.TemplateId, e.MetricKey, e.EffectiveFrom }).IsUnique();
        });
    }

    private static void ConfigurePlacement(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Placement>(entity =>
        {
            entity.ToTable("placement");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Objective).HasConversion<int>();
            entity.Property(e => e.AssetType).HasMaxLength(50);
            entity.Property(e => e.CreativeCode).HasMaxLength(50);
            entity.Property(e => e.OsCode).HasMaxLength(50);
            entity.Property(e => e.UtmUrl).HasMaxLength(2000);
            entity.Property(e => e.ArtworkUrl).HasMaxLength(500);
            entity.Property(e => e.MediaCost).HasColumnType("numeric(12,2)");
            entity.Property(e => e.CpdInvestmentCost).HasColumnType("numeric(12,2)");
            entity.Property(e => e.Circulation).HasColumnType("numeric(12,2)");
            entity.Property(e => e.LiveMonths).HasColumnType("integer[]");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.Brand).WithMany(b => b.Placements).HasForeignKey(e => e.BrandId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Audience).WithMany(a => a.Placements).HasForeignKey(e => e.AudienceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Publisher).WithMany(p => p.Placements).HasForeignKey(e => e.PublisherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Template).WithMany(t => t.Placements).HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.TargetCourse).WithMany().HasForeignKey(e => e.TargetCourseId).OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.BrandId);
            entity.HasIndex(e => e.AudienceId);
            entity.HasIndex(e => e.PublisherId);
            entity.HasIndex(e => e.OsCode);
        });
    }

    private static void ConfigurePlacementKpi(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlacementKpi>(entity =>
        {
            entity.ToTable("placement_kpi");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MetricKey).IsRequired().HasMaxLength(50);
            entity.Property(e => e.TargetValue).HasColumnType("numeric(18,4)");
            entity.HasOne(e => e.Placement).WithMany(p => p.Kpis).HasForeignKey(e => e.PlacementId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.PlacementId, e.MetricKey }).IsUnique();
        });
    }

    private static void ConfigurePlacementActual(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlacementActual>(entity =>
        {
            entity.ToTable("placement_actual");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MetricKey).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Value).HasColumnType("numeric(18,4)");
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.HasOne(e => e.Placement).WithMany(p => p.Actuals).HasForeignKey(e => e.PlacementId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.PlacementId, e.Year, e.Month, e.MetricKey }).IsUnique();
        });
    }

    private static void ConfigurePlacementComment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlacementComment>(entity =>
        {
            entity.ToTable("placement_comment");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Body).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.Placement).WithMany(p => p.Discussion).HasForeignKey(e => e.PlacementId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Author).WithMany().HasForeignKey(e => e.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Parent).WithMany(p => p.Replies).HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.PlacementId);
        });
    }

    private static void ConfigureEducationCourse(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EducationCourse>(entity =>
        {
            entity.ToTable("education_course");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CourseType).HasConversion<int>();
            entity.Property(e => e.Presenter).HasMaxLength(255);
            entity.Property(e => e.CpdInvestmentCost).HasColumnType("numeric(12,2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Audience).WithMany().HasForeignKey(e => e.AudienceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Publisher).WithMany().HasForeignKey(e => e.PublisherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BrandId, e.AudienceId });
        });
    }

    private static void ConfigureEducationCourseStatus(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EducationCourseStatus>(entity =>
        {
            entity.ToTable("education_course_status");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Course).WithMany(c => c.MonthlyStatus).HasForeignKey(e => e.CourseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.CourseId, e.Year, e.Month }).IsUnique();
        });
    }

    private static void ConfigureUtmLink(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UtmLink>(entity =>
        {
            entity.ToTable("utm_link");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Destination).IsRequired().HasMaxLength(500);
            entity.Property(e => e.AssetType).HasMaxLength(50);
            entity.Property(e => e.CreativeCode).HasMaxLength(50);
            entity.Property(e => e.OsCode).HasMaxLength(50);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.LiveMonths).HasColumnType("integer[]");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.Client).WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Placement).WithMany().HasForeignKey(e => e.PlacementId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Course).WithMany().HasForeignKey(e => e.CourseId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.ClientId);
            entity.HasIndex(e => e.OsCode);
        });
    }

    private static void ConfigureUtmLinkClicks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UtmLinkClicks>(entity =>
        {
            entity.ToTable("utm_link_clicks");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.UtmLink).WithMany(u => u.MonthlyClicks).HasForeignKey(e => e.UtmLinkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UtmLinkId, e.Year, e.Month }).IsUnique();
        });
    }

    private static void ConfigureMonthSnapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MonthSnapshot>(entity =>
        {
            entity.ToTable("month_snapshot");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Audience).WithMany().HasForeignKey(e => e.AudienceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.BrandId, e.AudienceId, e.Year, e.Month }).IsUnique();
        });
    }

    private static void ConfigureAppUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_user");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.EntraId).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.EntraId);
        });
    }

    private static void ConfigureUserClient(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserClient>(entity =>
        {
            entity.ToTable("user_client");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.User).WithMany(u => u.Clients).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Client).WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.ClientId }).IsUnique();
        });
    }

    private static void ConfigureMagicLink(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MagicLink>(entity =>
        {
            entity.ToTable("magic_link");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(128);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.Email);
        });
    }

    private static void ConfigureUserRole(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_role");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.User).WithMany(u => u.Roles).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.Role }).IsUnique();
        });
    }

    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.EntityId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(30);
            entity.Property(e => e.BeforeJson).HasColumnType("jsonb");
            entity.Property(e => e.AfterJson).HasColumnType("jsonb");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.UserId);
        });
    }
}
