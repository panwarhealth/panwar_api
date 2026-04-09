using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Panwar.Api.Data;

/// <summary>
/// Design-time factory used by `dotnet ef migrations add` / `dotnet ef database update`.
/// Reads connection string from local.settings.json (the same file the Functions host uses)
/// so dev and migrations stay in sync.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("local.settings.json", optional: false)
            .Build();

        var connectionString = configuration["Values:DATABASE_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("DATABASE_CONNECTION_STRING not configured in local.settings.json");

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(dataSource, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.SchemaName));

        return new AppDbContext(optionsBuilder.Options);
    }
}
