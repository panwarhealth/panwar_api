namespace Panwar.Api.Models;

public class TsEntry
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public Guid JobId { get; set; }
    public Guid TaskId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public string Notes { get; set; } = "";
    public DateTime? TimerStartedAt { get; set; }
    public long? HarvestId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public TsPerson Person { get; set; } = null!;
    public TsJob Job { get; set; } = null!;
    public TsTask Task { get; set; } = null!;
}
