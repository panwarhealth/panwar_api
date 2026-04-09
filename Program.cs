using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Panwar.Api.Data;
using Panwar.Api.Infrastructure.CloudflareR2;
using Panwar.Api.Services;
using Panwar.Api.Services.Seed;
using Panwar.Api.Shared.Middleware;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(app =>
    {
        app.UseMiddleware<CorrelationMiddleware>();
        app.UseMiddleware<AuthenticationMiddleware>();
        app.UseMiddleware<RateLimitMiddleware>();
    })
    .ConfigureLogging(logging =>
    {
        // Remove the default ApplicationInsights filter that blocks Information-level logs
        logging.Services.Configure<LoggerFilterOptions>(options =>
        {
            LoggerFilterRule? defaultRule = options.Rules.FirstOrDefault(rule =>
                rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
            if (defaultRule is not null)
            {
                options.Rules.Remove(defaultRule);
            }
        });
    })
    .ConfigureServices((context, services) =>
    {
        // camelCase JSON output across all WriteAsJsonAsync calls so the React frontends
        // (which read camelCase) don't silently see undefined for every field.
        services.Configure<WorkerOptions>(workerOptions =>
        {
            workerOptions.Serializer = new JsonObjectSerializer(
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });
        });

        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var connectionString = context.Configuration["DATABASE_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("DATABASE_CONNECTION_STRING not configured");

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.SchemaName)));

        services.AddHttpClient();

        // Auth + identity
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IMagicLinkService, MagicLinkService>();
        services.AddScoped<IEmailService, EmailService>();

        // Infrastructure
        services.AddScoped<ICloudflareR2Service, CloudflareR2Service>();

        // Read models for the client portal
        services.AddScoped<IDashboardService, DashboardService>();

        // Seed (dev-only — temporary endpoint, will be removed once import UI exists)
        services.AddScoped<IReckittSeedService, ReckittSeedService>();
    })
    .Build();

host.Run();
