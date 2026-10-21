namespace Panwar.Api.Models;

public class TsJobPerson
{
    public Guid JobId { get; set; }
    public Guid PersonId { get; set; }
    public bool IsManager { get; set; }

    public TsJob Job { get; set; } = null!;
    public TsPerson Person { get; set; } = null!;
}
