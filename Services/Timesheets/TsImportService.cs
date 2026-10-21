using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public interface ITsImportService
{
    Task<TsImportResultDto> ImportHarvestAsync(IReadOnlyCollection<string> leaveJobCodes, CancellationToken cancellationToken = default);
    Task<TsImportResultDto> ImportForecastAsync(string timeOffJobCode, CancellationToken cancellationToken = default);
}

public class TsImportService : ITsImportService
{
    public const string HttpClientName = "harvest";
    private const string HarvestUrl = "https://api.harvestapp.com/v2/";
    private const string ForecastUrl = "https://api.forecastapp.com/";
    private const string ForecastTimeOff = "Time Off";
    private const int BookingDaysAhead = 730;
    private const int ForecastChunkDays = 180;
    private const string PeopleManagerRole = "people_manager";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly Regex PrefixedCode = new("^([A-Za-z]+)[0-9]+$", RegexOptions.Compiled);
    private static readonly string[] WeekDays = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TsImportService> _logger;

    public TsImportService(AppDbContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<TsImportService> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TsImportResultDto> ImportHarvestAsync(IReadOnlyCollection<string> leaveJobCodes, CancellationToken cancellationToken = default)
    {
        var http = Client(HarvestUrl, "Harvest-Account-ID", "HARVEST_ACCOUNT_ID");
        var leave = leaveJobCodes.Select(c => c.Trim().ToUpperInvariant()).ToHashSet();

        var clients = await PagesAsync<HvClient>(http, "clients", cancellationToken);
        var clientIds = await UpsertAsync(_context.TsClients, clients, c => c.Id, () => new TsClient { Id = Guid.NewGuid(), Name = "", CreatedAt = DateTime.UtcNow }, (e, c) =>
        {
            e.Name = c.Name;
            e.Archived = !c.IsActive;
        }, cancellationToken);

        var tasks = await PagesAsync<HvTask>(http, "tasks", cancellationToken);
        var taskIds = await UpsertAsync(_context.TsTasks, tasks, t => t.Id, () => new TsTask { Id = Guid.NewGuid(), Name = "", CreatedAt = DateTime.UtcNow }, (e, t) =>
        {
            e.Name = t.Name;
            e.IsCommon = t.IsDefault && t.IsActive;
            e.Archived = !t.IsActive;
        }, cancellationToken);

        var users = await PagesAsync<HvUser>(http, "users", cancellationToken);
        var personIds = await UpsertPeopleAsync(users, cancellationToken);
        await ImportPeopleManagersAsync(http, users, personIds, cancellationToken);

        var projects = await PagesAsync<HvProject>(http, "projects", cancellationToken);
        var jobIds = await UpsertAsync(_context.TsJobs, projects, p => p.Id, () => new TsJob { Id = Guid.NewGuid(), Code = "", Title = "", CreatedAt = DateTime.UtcNow }, (e, p) =>
        {
            e.ClientId = clientIds[p.Client.Id];
            e.Code = (p.Code ?? "").Trim();
            e.Title = p.Name.Trim();
            e.Archived = !p.IsActive;
            e.IsLeave = leave.Contains(e.Code.ToUpperInvariant());
            e.AlertPercent = p.NotifyWhenOverBudget ? (int)Math.Round(p.OverBudgetNotificationPercentage ?? 80) : null;
            e.OpenedOn = p.StartsOn ?? DateOnly.FromDateTime(p.CreatedAt);
            e.CreatedAt = p.CreatedAt;
        }, cancellationToken);
        await SetClientPrefixesAsync(projects, clientIds, cancellationToken);

        var importedJobs = jobIds.Values.ToList();
        await _context.TsJobTasks.Where(t => importedJobs.Contains(t.JobId)).ExecuteDeleteAsync(cancellationToken);
        await _context.TsJobPeople.Where(p => importedJobs.Contains(p.JobId)).ExecuteDeleteAsync(cancellationToken);

        var taskAssignments = await PagesAsync<HvTaskAssignment>(http, "task_assignments?is_active=true", cancellationToken);
        _context.TsJobTasks.AddRange(taskAssignments
            .Where(a => jobIds.ContainsKey(a.Project.Id) && taskIds.ContainsKey(a.Task.Id))
            .DistinctBy(a => (a.Project.Id, a.Task.Id))
            .Select(a => new TsJobTask { JobId = jobIds[a.Project.Id], TaskId = taskIds[a.Task.Id], BudgetHours = a.Budget }));

        var userAssignments = await PagesAsync<HvUserAssignment>(http, "user_assignments?is_active=true", cancellationToken);
        _context.TsJobPeople.AddRange(userAssignments
            .Where(a => jobIds.ContainsKey(a.Project.Id) && personIds.ContainsKey(a.User.Id))
            .DistinctBy(a => (a.Project.Id, a.User.Id))
            .Select(a => new TsJobPerson { JobId = jobIds[a.Project.Id], PersonId = personIds[a.User.Id], IsManager = a.IsProjectManager }));
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();

        var entries = await ImportEntriesAsync(http, personIds, jobIds, taskIds, cancellationToken);
        return new TsImportResultDto(clientIds.Count, taskIds.Count, personIds.Count, jobIds.Count, entries, 0);
    }

    public async Task<TsImportResultDto> ImportForecastAsync(string timeOffJobCode, CancellationToken cancellationToken = default)
    {
        var http = Client(ForecastUrl, "Forecast-Account-ID", "FORECAST_ACCOUNT_ID");
        var code = timeOffJobCode.Trim().ToUpperInvariant();
        var timeOffJob = await _context.TsJobs.Where(j => j.Code.ToUpper() == code).Select(j => (Guid?)j.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new TsValidationException($"No job has the code {code}, import Harvest first");

        var people = (await GetAsync<FcPeople>(http, "people", cancellationToken)).People;
        var byHarvestUser = await _context.TsPeople.Where(p => p.HarvestId != null).ToDictionaryAsync(p => p.HarvestId!.Value, cancellationToken);
        var personIds = new Dictionary<long, Guid>();
        foreach (var fp in people.Where(p => p.HarvestUserId is not null && byHarvestUser.ContainsKey(p.HarvestUserId.Value)))
        {
            var person = byHarvestUser[fp.HarvestUserId!.Value];
            var workingDays = WeekDays.Select(d => fp.WorkingDays.GetValueOrDefault(d)).ToList();
            var perDay = workingDays.Count(w => w) == 0 ? 0 : Math.Round(fp.WeeklyCapacity / 3600m / workingDays.Count(w => w), 2);
            person.ForecastId = fp.Id;
            person.DayHours = workingDays.Select(w => w ? perDay : 0).ToArray();
            personIds[fp.Id] = person.Id;
        }
        await _context.SaveChangesAsync(cancellationToken);

        var projects = (await GetAsync<FcProjects>(http, "projects", cancellationToken)).Projects;
        var jobsByHarvest = await _context.TsJobs.Where(j => j.HarvestId != null).ToDictionaryAsync(j => j.HarvestId!.Value, j => j.Id, cancellationToken);
        var jobsByCode = (await _context.TsJobs.Where(j => j.Code != "").Select(j => new { j.Code, j.Id }).ToListAsync(cancellationToken))
            .GroupBy(j => j.Code.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Id);
        var jobIds = new Dictionary<long, Guid>();
        foreach (var fp in projects)
        {
            if (fp.HarvestId is { } hid && jobsByHarvest.TryGetValue(hid, out var byId)) jobIds[fp.Id] = byId;
            else if (fp.Code is { } c && jobsByCode.TryGetValue(c.ToUpperInvariant(), out var byCode)) jobIds[fp.Id] = byCode;
            else if (fp.HarvestId is null && fp.Name == ForecastTimeOff) jobIds[fp.Id] = timeOffJob;
        }

        var from = TsCalendar.WeekStart(TsCalendar.Today);
        var to = from.AddDays(BookingDaysAhead);
        var all = new List<FcAssignment>();
        for (var chunk = from; chunk <= to; chunk = chunk.AddDays(ForecastChunkDays))
        {
            var chunkEnd = chunk.AddDays(ForecastChunkDays - 1);
            all.AddRange((await GetAsync<FcAssignments>(http, $"assignments?start_date={chunk:yyyy-MM-dd}&end_date={chunkEnd:yyyy-MM-dd}", cancellationToken)).Assignments);
        }
        all = all.DistinctBy(a => a.Id).ToList();
        var assignments = all
            .Where(a => a.PersonId is { } pid && personIds.ContainsKey(pid) && jobIds.ContainsKey(a.ProjectId) && a.Allocation is > 0)
            .ToList();
        var skipped = all.Count - assignments.Count;
        if (skipped > 0) _logger.LogWarning("Forecast import skipped {Count} bookings with no matching person or job", skipped);

        var existing = await _context.TsBookings.Where(b => b.ForecastId != null).ToDictionaryAsync(b => b.ForecastId!.Value, cancellationToken);
        foreach (var a in assignments)
        {
            if (!existing.TryGetValue(a.Id, out var booking))
            {
                booking = new TsBooking { Id = Guid.NewGuid(), ForecastId = a.Id, CreatedAt = DateTime.UtcNow };
                _context.TsBookings.Add(booking);
            }
            booking.PersonId = personIds[a.PersonId!.Value];
            booking.JobId = jobIds[a.ProjectId];
            booking.StartDate = a.StartDate;
            booking.EndDate = a.EndDate;
            booking.HoursPerDay = Math.Round(a.Allocation!.Value / 3600m, 2);
            booking.Note = string.IsNullOrWhiteSpace(a.Notes) ? null : a.Notes.Trim();
            booking.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync(cancellationToken);
        return new TsImportResultDto(0, 0, personIds.Count, jobIds.Count, 0, assignments.Count);
    }

    private async Task<Dictionary<long, Guid>> UpsertPeopleAsync(IReadOnlyList<HvUser> users, CancellationToken cancellationToken)
    {
        var people = await _context.TsPeople.ToListAsync(cancellationToken);
        var ids = new Dictionary<long, Guid>();
        foreach (var u in users)
        {
            var email = u.Email.Trim().ToLowerInvariant();
            var person = people.FirstOrDefault(p => p.HarvestId == u.Id) ?? people.FirstOrDefault(p => p.Email == email);
            if (person is null)
            {
                person = new TsPerson { Id = Guid.NewGuid(), FirstName = "", CreatedAt = DateTime.UtcNow };
                _context.TsPeople.Add(person);
                people.Add(person);
            }
            var perDay = Math.Round(u.WeeklyCapacity / 3600m / 5, 2);
            person.HarvestId = u.Id;
            person.FirstName = u.FirstName.Trim();
            person.LastName = u.LastName.Trim();
            person.Email = email;
            person.Title = u.Roles.Count == 0 ? null : string.Join(", ", u.Roles);
            person.IsContractor = u.IsContractor;
            person.Archived = !u.IsActive;
            person.DayHours = [perDay, perDay, perDay, perDay, perDay, 0, 0];
            person.UpdatedAt = DateTime.UtcNow;
            ids[u.Id] = person.Id;
        }
        await _context.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private async Task ImportPeopleManagersAsync(HttpClient http, IReadOnlyList<HvUser> users,
        IReadOnlyDictionary<long, Guid> personIds, CancellationToken cancellationToken)
    {
        var imported = personIds.Values.ToList();
        await _context.TsPersonManagers.Where(m => imported.Contains(m.ManagerId)).ExecuteDeleteAsync(cancellationToken);
        foreach (var manager in users.Where(u => u.AccessRoles.Contains(PeopleManagerRole)))
        {
            var teammates = await PagesAsync<HvRef>(http, $"users/{manager.Id}/teammates", cancellationToken, "teammates");
            _context.TsPersonManagers.AddRange(teammates
                .Where(t => personIds.ContainsKey(t.Id))
                .Select(t => new TsPersonManager { ManagerId = personIds[manager.Id], PersonId = personIds[t.Id] }));
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<long, Guid>> UpsertAsync<TEntity, TSource>(
        DbSet<TEntity> set, IReadOnlyList<TSource> sources, Func<TSource, long> harvestId,
        Func<TEntity> create, Action<TEntity, TSource> apply, CancellationToken cancellationToken)
        where TEntity : class, IHarvestRecord
    {
        var existing = await set.Where(e => e.HarvestId != null).ToDictionaryAsync(e => e.HarvestId!.Value, cancellationToken);
        var ids = new Dictionary<long, Guid>();
        foreach (var source in sources)
        {
            var id = harvestId(source);
            if (!existing.TryGetValue(id, out var entity))
            {
                entity = create();
                entity.HarvestId = id;
                set.Add(entity);
            }
            apply(entity, source);
            entity.UpdatedAt = DateTime.UtcNow;
            ids[id] = entity.Id;
        }
        await _context.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private async Task SetClientPrefixesAsync(IReadOnlyList<HvProject> projects, IReadOnlyDictionary<long, Guid> clientIds, CancellationToken cancellationToken)
    {
        var latestCodes = projects
            .Where(p => !string.IsNullOrWhiteSpace(p.Code))
            .GroupBy(p => clientIds[p.Client.Id])
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First().Code!.Trim());

        var clients = await _context.TsClients.Where(c => c.CodePrefix == null && !c.SuppliesOwnCodes).ToListAsync(cancellationToken);
        foreach (var client in clients.Where(c => latestCodes.ContainsKey(c.Id)))
        {
            var code = latestCodes[client.Id];
            var match = PrefixedCode.Match(code);
            if (match.Success) client.CodePrefix = match.Groups[1].Value.ToUpperInvariant();
            else if (code.All(char.IsDigit)) client.SuppliesOwnCodes = true;
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> ImportEntriesAsync(HttpClient http, IReadOnlyDictionary<long, Guid> personIds,
        IReadOnlyDictionary<long, Guid> jobIds, IReadOnlyDictionary<long, Guid> taskIds, CancellationToken cancellationToken)
    {
        var count = 0;
        var seen = new List<long>();
        string? url = "time_entries?per_page=2000";
        while (url is not null)
        {
            var page = await GetAsync<HvPage<HvEntry>>(http, url, cancellationToken);
            var items = page.Items("time_entries");
            var pageIds = items.Select(e => e.Id).ToList();
            seen.AddRange(pageIds);
            var existing = await _context.TsEntries.Where(e => pageIds.Contains(e.HarvestId!.Value)).ToDictionaryAsync(e => e.HarvestId!.Value, cancellationToken);
            foreach (var e in items)
            {
                if (!existing.TryGetValue(e.Id, out var entry))
                {
                    entry = new TsEntry { Id = Guid.NewGuid(), HarvestId = e.Id };
                    _context.TsEntries.Add(entry);
                }
                entry.PersonId = personIds[e.User.Id];
                entry.JobId = jobIds[e.Project.Id];
                entry.TaskId = taskIds[e.Task.Id];
                entry.Date = e.SpentDate;
                entry.Hours = e.Hours;
                entry.Notes = (e.Notes ?? "").Trim();
                entry.TimerStartedAt = null;
                entry.CreatedAt = e.CreatedAt;
                entry.UpdatedAt = e.UpdatedAt;
            }
            await _context.SaveChangesAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            count += items.Count;
            _logger.LogInformation("Imported {Count} Harvest time entries", count);
            url = page.Links.Next is { } next ? next[HarvestUrl.Length..] : null;
        }
        var removed = await _context.TsEntries
            .Where(x => x.HarvestId != null && !seen.Contains(x.HarvestId.Value))
            .ExecuteDeleteAsync(cancellationToken);
        _logger.LogInformation("Removed {Count} time entries deleted in Harvest", removed);
        return count;
    }

    private HttpClient Client(string baseUrl, string accountHeader, string accountSetting)
    {
        var token = _configuration["HARVEST_TOKEN"];
        var account = _configuration[accountSetting];
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(account))
            throw new TsValidationException($"HARVEST_TOKEN and {accountSetting} need to be set to import");

        var http = _httpClientFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(baseUrl);
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        http.DefaultRequestHeaders.Add(accountHeader, account);
        http.DefaultRequestHeaders.Add("User-Agent", "Panwar Timesheets");
        return http;
    }

    private static async Task<T> GetAsync<T>(HttpClient http, string url, CancellationToken cancellationToken)
        => await http.GetFromJsonAsync<T>(url, Json, cancellationToken) ?? throw new InvalidOperationException($"Empty response from {url}");

    private static async Task<IReadOnlyList<T>> PagesAsync<T>(HttpClient http, string endpoint, CancellationToken cancellationToken, string? key = null)
    {
        key ??= endpoint.Split('?')[0];
        var all = new List<T>();
        string? url = endpoint + (endpoint.Contains('?') ? "&" : "?") + "per_page=2000";
        while (url is not null)
        {
            var page = await GetAsync<HvPage<T>>(http, url, cancellationToken);
            all.AddRange(page.Items(key));
            url = page.Links.Next is { } next ? next[HarvestUrl.Length..] : null;
        }
        return all;
    }

    private sealed class HvPage<T>
    {
        public HvLinks Links { get; set; } = new();
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement> Data { get; set; } = [];

        public IReadOnlyList<T> Items(string key) => Data[key].Deserialize<List<T>>(Json) ?? [];
    }

    private sealed class HvLinks { public string? Next { get; set; } }
    private sealed class HvRef { public long Id { get; set; } }
    private sealed class HvClient { public long Id { get; set; } public string Name { get; set; } = ""; public bool IsActive { get; set; } }
    private sealed class HvTask { public long Id { get; set; } public string Name { get; set; } = ""; public bool IsDefault { get; set; } public bool IsActive { get; set; } }

    private sealed class HvUser
    {
        public long Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public bool IsContractor { get; set; }
        public bool IsActive { get; set; }
        public int WeeklyCapacity { get; set; }
        public List<string> Roles { get; set; } = [];
        public List<string> AccessRoles { get; set; } = [];
    }

    private sealed class HvProject
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? Code { get; set; }
        public bool IsActive { get; set; }
        public bool NotifyWhenOverBudget { get; set; }
        public decimal? OverBudgetNotificationPercentage { get; set; }
        public DateOnly? StartsOn { get; set; }
        public DateTime CreatedAt { get; set; }
        public HvRef Client { get; set; } = new();
    }

    private sealed class HvTaskAssignment { public HvRef Project { get; set; } = new(); public HvRef Task { get; set; } = new(); public decimal? Budget { get; set; } }
    private sealed class HvUserAssignment { public HvRef Project { get; set; } = new(); public HvRef User { get; set; } = new(); public bool IsProjectManager { get; set; } }

    private sealed class HvEntry
    {
        public long Id { get; set; }
        public DateOnly SpentDate { get; set; }
        public decimal Hours { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public HvRef User { get; set; } = new();
        public HvRef Project { get; set; } = new();
        public HvRef Task { get; set; } = new();
    }

    private sealed class FcPeople { public List<FcPerson> People { get; set; } = []; }

    private sealed class FcPerson
    {
        public long Id { get; set; }
        public long? HarvestUserId { get; set; }
        public int WeeklyCapacity { get; set; }
        public Dictionary<string, bool> WorkingDays { get; set; } = [];
    }

    private sealed class FcProjects { public List<FcProject> Projects { get; set; } = []; }
    private sealed class FcProject { public long Id { get; set; } public string Name { get; set; } = ""; public string? Code { get; set; } public long? HarvestId { get; set; } }
    private sealed class FcAssignments { public List<FcAssignment> Assignments { get; set; } = []; }

    private sealed class FcAssignment
    {
        public long Id { get; set; }
        public long? PersonId { get; set; }
        public long ProjectId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int? Allocation { get; set; }
        public string? Notes { get; set; }
    }
}
