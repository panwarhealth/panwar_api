namespace Panwar.Api.Models;

/// <summary>
/// A named education dashboard page for a client (e.g. "Pharmacy Education",
/// "GP Education"). A client can have any number of pages; each holds one or
/// more completion bar charts. Modelled on the Reckitt workbook's education
/// tabs, generalised so staff can add and name pages freely.
/// </summary>
public class EducationPage
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Client Client { get; set; } = null!;
    public ICollection<EducationChart> Charts { get; set; } = new List<EducationChart>();
}
