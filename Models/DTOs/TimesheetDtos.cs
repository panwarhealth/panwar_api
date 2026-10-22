namespace Panwar.Api.Models.DTOs;

public sealed record TsViewerDto(Guid PersonId, string Name, string Level);

public sealed record TsNamedDto(Guid Id, string Name);

public sealed record TsEntryDto(
    Guid Id,
    Guid PersonId,
    DateOnly Date,
    Guid JobId,
    string JobCode,
    string JobTitle,
    string ClientName,
    Guid TaskId,
    string TaskName,
    decimal Hours,
    string Notes,
    DateTime? TimerStartedAt);

public sealed record TsStarDto(Guid JobId, string JobCode, string JobTitle, string ClientName, Guid TaskId, string TaskName);

public sealed record TsTimeDto(IReadOnlyList<TsEntryDto> Entries, IReadOnlyList<TsStarDto> Stars);

public sealed record TsPickJobDto(Guid Id, string Code, string Title, string ClientName, IReadOnlyList<TsNamedDto> Tasks);

public class TsEntryWriteRequest
{
    public Guid? PersonId { get; set; }
    public DateOnly Date { get; set; }
    public Guid JobId { get; set; }
    public Guid TaskId { get; set; }
    public decimal Hours { get; set; }
    public string Notes { get; set; } = "";
    public bool StartTimer { get; set; }
}

public class TsStopTimerRequest
{
    public string Notes { get; set; } = "";
}

public class TsStarRequest
{
    public Guid JobId { get; set; }
    public Guid TaskId { get; set; }
    public bool Starred { get; set; }
}

public sealed record TsJobRowDto(Guid Id, string Code, string Title, Guid ClientId, string ClientName, decimal Spent, bool Archived, bool IsLeave, string? Color, IReadOnlyList<Guid> ManagerIds);

public sealed record TsColorLabelDto(string Key, string Name);

public sealed record TsJobTaskDto(Guid TaskId, string Name, decimal? Budget, decimal Spent);

public sealed record TsJobPersonDto(Guid PersonId, string Name, bool IsManager, bool CanLog, decimal Spent);

public sealed record TsWeekHoursDto(DateOnly WeekStart, decimal Hours);

public sealed record TsJobDetailDto(
    Guid Id,
    string Code,
    string Title,
    Guid ClientId,
    string ClientName,
    bool Archived,
    bool IsLeave,
    bool Everyone,
    int? AlertPercent,
    string? TrelloCardUrl,
    DateOnly OpenedOn,
    decimal Spent,
    decimal? Budget,
    IReadOnlyList<TsJobTaskDto> Tasks,
    IReadOnlyList<TsJobPersonDto> People,
    IReadOnlyList<TsWeekHoursDto> Weeks,
    string? Color);

public class TsJobTaskWrite
{
    public Guid TaskId { get; set; }
    public decimal? BudgetHours { get; set; }
}

public class TsJobPersonWrite
{
    public Guid PersonId { get; set; }
    public bool IsManager { get; set; }
}

public class TsJobWriteRequest
{
    public Guid ClientId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsLeave { get; set; }
    public string? Color { get; set; }
    public int? AlertPercent { get; set; }
    public List<TsJobTaskWrite> Tasks { get; set; } = [];
    public List<TsJobPersonWrite> People { get; set; } = [];
    public bool CreateTrelloCard { get; set; }
}

public sealed record TsNextCodeDto(string? Code, string? LastJob, string? LastColor);

public class TsArchiveRequest
{
    public bool Archived { get; set; }
}

public sealed record TsClientDto(Guid Id, string Name, string? CodePrefix, bool SuppliesOwnCodes, string? NextCode);

public class TsClientWriteRequest
{
    public string Name { get; set; } = "";
    public string? CodePrefix { get; set; }
    public bool SuppliesOwnCodes { get; set; }
}

public sealed record TsTaskDto(Guid Id, string Name, bool IsCommon, bool Archived);

public class TsTaskWriteRequest
{
    public string Name { get; set; } = "";
    public bool IsCommon { get; set; }
    public bool Archived { get; set; }
}

public sealed record TsCapacityChangeDto(DateOnly Date, decimal Hours, string? Note);

public sealed record TsPersonDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Title,
    bool IsContractor,
    bool IsPlaceholder,
    bool Archived,
    IReadOnlyList<decimal> DayHours,
    IReadOnlyList<TsCapacityChangeDto> CapacityChanges,
    IReadOnlyList<Guid> ManagedPersonIds);

public class TsPersonWriteRequest
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public string? Title { get; set; }
    public bool IsContractor { get; set; }
    public bool IsPlaceholder { get; set; }
    public bool Archived { get; set; }
    public List<Guid> ManagedPersonIds { get; set; } = [];
}

public class TsCapacityChangeWrite
{
    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public string? Note { get; set; }
}

public class TsCapacityWriteRequest
{
    public List<decimal> DayHours { get; set; } = [];
    public List<TsCapacityChangeWrite> Changes { get; set; } = [];
}

public sealed record TsTeamRowDto(
    Guid PersonId,
    string Name,
    string? Title,
    string? Role,
    bool IsContractor,
    decimal Hours,
    decimal BaseCapacity,
    decimal Leave,
    bool NotDone);

public sealed record TsScheduleWeekDto(DateOnly Start, decimal Capacity, decimal Booked, decimal Off, int? Percent);

public sealed record TsScheduleDayDto(DateOnly Date, decimal Capacity, decimal Booked, decimal Off);

public sealed record TsSchedulePersonDto(
    Guid Id,
    string Name,
    string? Title,
    bool IsPlaceholder,
    IReadOnlyList<TsScheduleWeekDto> Weeks,
    IReadOnlyList<TsScheduleDayDto> Days);

public sealed record TsScheduleJobDto(Guid JobId, string JobCode, string JobTitle, string ClientName, bool IsLeave, string? Color, IReadOnlyList<decimal> Weeks);

public sealed record TsBookingDto(
    Guid Id,
    Guid PersonId,
    Guid JobId,
    string JobCode,
    string JobTitle,
    string ClientName,
    bool IsLeave,
    Guid? TaskId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal HoursPerDay,
    bool OnDaysOff,
    string? Note,
    int RepeatWeeks,
    DateTime UpdatedAt,
    string? UpdatedByName,
    string? JobColor);

public sealed record TsScheduleDto(IReadOnlyList<TsSchedulePersonDto> People, IReadOnlyList<TsScheduleJobDto> Jobs, IReadOnlyList<TsBookingDto> Bookings);

public sealed record TsJobHoursDto(Guid JobId, decimal Hours, IReadOnlyList<Guid> PersonIds);

public class TsShiftRequest
{
    public int Days { get; set; }
}

public class TsBookingMoveRequest
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public class TsBookingWriteRequest
{
    public Guid PersonId { get; set; }
    public Guid JobId { get; set; }
    public Guid? TaskId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal HoursPerDay { get; set; }
    public bool OnDaysOff { get; set; }
    public string? Note { get; set; }
    public int RepeatWeeks { get; set; } = 1;
}

public sealed record TsSummaryRowDto(Guid Id, string Name, decimal Hours);

public sealed record TsSummaryDto(decimal Total, IReadOnlyList<TsSummaryRowDto> Rows);

public sealed record TsDetailedDto(decimal Total, int Count, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record TsReportOptionsDto(
    IReadOnlyList<TsNamedDto> Clients,
    IReadOnlyList<TsNamedDto> Jobs,
    IReadOnlyList<TsNamedDto> Tasks,
    IReadOnlyList<TsNamedDto> People);

public sealed record TsSavedReportDto(Guid Id, string Name, string Query, DateTime CreatedAt);

public class TsSavedReportWriteRequest
{
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
}

public sealed record TsImportResultDto(int Clients, int Tasks, int People, int Jobs, int Entries, int Bookings);

public class TsHarvestImportRequest
{
    public List<string> LeaveJobCodes { get; set; } = [];
}

public class TsForecastImportRequest
{
    public string TimeOffJobCode { get; set; } = "";
}
