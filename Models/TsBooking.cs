namespace Panwar.Api.Models;

public class TsBooking
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public Guid JobId { get; set; }
    public Guid? TaskId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal HoursPerDay { get; set; }
    public string? Note { get; set; }
    public Guid? RepeatSetId { get; set; }
    public long? ForecastId { get; set; }
    public Guid? UpdatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public TsPerson Person { get; set; } = null!;
    public TsJob Job { get; set; } = null!;
    public TsTask? Task { get; set; }
    public TsPerson? UpdatedBy { get; set; }
}
