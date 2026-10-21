using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Timesheets;

public sealed record TsViewer(Guid PersonId, string Name, TsLevel Level)
{
    public bool IsAdmin => Level == TsLevel.Admin;
    public bool IsManager => Level >= TsLevel.Manager;
}

public interface ITsViewerService
{
    Task<TsViewer> GetAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default);
}

public static class TsRoles
{
    public const string Admin = "timesheets-admin";
    public const string Manager = "timesheets-manager";
    private const string PortalAdmin = "panwar-admin";

    public static TsLevel LevelFor(IReadOnlyCollection<string> roles)
    {
        bool Has(string role) => roles.Contains(role, StringComparer.OrdinalIgnoreCase);
        if (Has(Admin) || Has(PortalAdmin)) return TsLevel.Admin;
        return Has(Manager) ? TsLevel.Manager : TsLevel.Member;
    }
}

public class TsViewerService : ITsViewerService
{
    private readonly AppDbContext _context;

    public TsViewerService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<TsViewer> GetAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId, cancellationToken);
        var email = user.Email.ToLowerInvariant();
        var person = await _context.TsPeople.FirstOrDefaultAsync(p => p.Email == email, cancellationToken)
            ?? await CreatePersonAsync(email, user.Name, cancellationToken);
        return new TsViewer(person.Id, TsNames.Full(person), TsRoles.LevelFor(roles));
    }

    private async Task<TsPerson> CreatePersonAsync(string email, string? name, CancellationToken cancellationToken)
    {
        var parts = (name ?? email.Split('@')[0]).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var person = new TsPerson
        {
            Id = Guid.NewGuid(),
            FirstName = parts.ElementAtOrDefault(0) ?? email,
            LastName = parts.ElementAtOrDefault(1) ?? "",
            Email = email,
            DayHours = TsCalendar.StandardWeek(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.TsPeople.Add(person);
        await _context.SaveChangesAsync(cancellationToken);
        return person;
    }
}

public static class TsNames
{
    public static string Full(TsPerson person) => $"{person.FirstName} {person.LastName}".Trim();

    public static string Job(TsJob job) => $"[{job.Code}] {job.Title}";
}
