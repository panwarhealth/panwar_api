namespace Panwar.Api.Models;

/// <summary>
/// The analyst-written summary for a client's reporting year (the workbook's
/// "FY RESULTS" commentary). Shown on the client overview - as a results
/// summary once the year has actuals, or as plan notes for a planned year.
/// One row per (client, year).
/// </summary>
public class ClientYearSummary
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public int Year { get; set; }
    public required string Text { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public Client Client { get; set; } = null!;
}
