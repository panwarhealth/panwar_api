using System.Globalization;

namespace Panwar.Api.Services.Timesheets;

public sealed record TsDetailedRow(
    DateOnly Date,
    string Client,
    string Project,
    string ProjectCode,
    string Task,
    string Notes,
    decimal Hours,
    string FirstName,
    string LastName,
    string? Roles,
    bool IsContractor);

public sealed record TsDetailedColumn(string Header, Func<TsDetailedRow, object?> Value);

public static class TsDetailedReport
{
    private const string Currency = "Australian Dollar - AUD";

    public static readonly IReadOnlyList<TsDetailedColumn> Columns =
    [
        new("Date", r => r.Date),
        new("Client", r => r.Client),
        new("Project", r => r.Project),
        new("Project Code", r => r.ProjectCode),
        new("Task", r => r.Task),
        new("Notes", r => r.Notes),
        new("Hours", r => r.Hours),
        new("Billable?", _ => "Yes"),
        new("Invoiced?", _ => "No"),
        new("Approved?", _ => "No"),
        new("First Name", r => r.FirstName),
        new("Last Name", r => r.LastName),
        new("Employee Id", _ => null),
        new("Roles", r => r.Roles),
        new("Teams", _ => null),
        new("Employee?", r => r.IsContractor ? "No" : "Yes"),
        new("Billable Rate", _ => 0m),
        new("Billable Amount", _ => 0m),
        new("Cost Rate", _ => 0m),
        new("Cost Amount", _ => 0m),
        new("Currency", _ => Currency),
        new("External Reference URL", _ => null)
    ];

    public static string Display(object? value) => value switch
    {
        null => "",
        DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        decimal n => n.ToString("0.00", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
