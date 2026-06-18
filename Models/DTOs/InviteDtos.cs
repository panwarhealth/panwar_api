namespace Panwar.Api.Models.DTOs;

public sealed record InviteListItemDto(
    Guid Id,
    Guid RecipientUserId,
    string RecipientEmail,
    string? RecipientName,
    string Template,
    int Year,
    int? StartMonth,
    int? EndMonth,
    DateTime SentAt,
    int SendCount,
    DateTime? ClickedAt,
    DateTime? ViewedAt);

public sealed record InviteListResponse(IReadOnlyList<InviteListItemDto> Items);

public class SendInvitesRequest
{
    public string Template { get; set; } = "";
    public int Year { get; set; }
    public int? StartMonth { get; set; }
    public int? EndMonth { get; set; }
    public string PreviewMode { get; set; } = "stats";
    public string? PreviewNote { get; set; }
    public List<Guid> RecipientUserIds { get; set; } = new();
}

public static class PreviewModes
{
    public const string Stats = "stats";
    public const string Chart = "chart";
    public const string Summary = "summary";
    public const string Note = "note";
    public const string None = "none";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Stats, Chart, Summary, Note, None,
    };
}

public class TrackViewRequest
{
    public string Token { get; set; } = "";
}

public static class InviteTemplates
{
    public const string ReportReady = "report_ready";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ReportReady,
    };
}
