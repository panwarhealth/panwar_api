namespace Panwar.Api.Models;

public class TsSavedReport
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public required string Name { get; set; }
    public required string Query { get; set; }
    public DateTime CreatedAt { get; set; }

    public TsPerson Person { get; set; } = null!;
}
