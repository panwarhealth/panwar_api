namespace Panwar.Api.Models;

public class TsPerson
{
    public Guid Id { get; set; }
    public required string FirstName { get; set; }
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public string? Title { get; set; }
    public bool IsContractor { get; set; }
    public bool IsPlaceholder { get; set; }
    public bool Archived { get; set; }
    public decimal[] DayHours { get; set; } = [];
    public long? HarvestId { get; set; }
    public long? ForecastId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<TsCapacityChange> CapacityChanges { get; set; } = new List<TsCapacityChange>();
    public ICollection<TsPersonManager> Manages { get; set; } = new List<TsPersonManager>();
}
