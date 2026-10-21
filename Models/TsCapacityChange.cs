namespace Panwar.Api.Models;

public class TsCapacityChange
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public string? Note { get; set; }

    public TsPerson Person { get; set; } = null!;
}
