namespace Panwar.Api.Models;

public class TsPersonManager
{
    public Guid ManagerId { get; set; }
    public Guid PersonId { get; set; }

    public TsPerson Manager { get; set; } = null!;
    public TsPerson Person { get; set; } = null!;
}
