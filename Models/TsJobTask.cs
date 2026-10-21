namespace Panwar.Api.Models;

public class TsJobTask
{
    public Guid JobId { get; set; }
    public Guid TaskId { get; set; }
    public decimal? BudgetHours { get; set; }

    public TsJob Job { get; set; } = null!;
    public TsTask Task { get; set; } = null!;
}
