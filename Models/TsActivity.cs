namespace Panwar.Api.Models;

public enum TsActivityKind
{
    Created,
    Edited,
    Deleted,
    TimerStarted,
    TimerStopped
}

public class TsActivity
{
    public Guid Id { get; set; }
    public DateTime At { get; set; }
    public TsActivityKind Kind { get; set; }
    public Guid EntryId { get; set; }
    public Guid PersonId { get; set; }
    public Guid PerformedById { get; set; }
    public DateOnly Date { get; set; }
    public Guid JobId { get; set; }
    public Guid? FromJobId { get; set; }
    public Guid TaskId { get; set; }
    public decimal? HoursBefore { get; set; }
    public decimal Hours { get; set; }

    public TsPerson Person { get; set; } = null!;
    public TsPerson PerformedBy { get; set; } = null!;
    public TsJob Job { get; set; } = null!;
    public TsJob? FromJob { get; set; }
    public TsTask Task { get; set; } = null!;
}
