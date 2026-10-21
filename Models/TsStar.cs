namespace Panwar.Api.Models;

public class TsStar
{
    public Guid PersonId { get; set; }
    public Guid JobId { get; set; }
    public Guid TaskId { get; set; }

    public TsJob Job { get; set; } = null!;
    public TsTask Task { get; set; } = null!;
}
