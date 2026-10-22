namespace Panwar.Api.Models;

public class TsJob : IHarvestRecord
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public required string Code { get; set; }
    public required string Title { get; set; }
    public bool Archived { get; set; }
    public bool IsLeave { get; set; }
    public string? Color { get; set; }
    public bool Everyone { get; set; }
    public int? AlertPercent { get; set; }
    public DateTime? AlertSentAt { get; set; }
    public DateOnly OpenedOn { get; set; }
    public string? TrelloCardUrl { get; set; }
    public long? HarvestId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public TsClient Client { get; set; } = null!;
    public ICollection<TsJobTask> Tasks { get; set; } = new List<TsJobTask>();
    public ICollection<TsJobPerson> People { get; set; } = new List<TsJobPerson>();
}
