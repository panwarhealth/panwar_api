namespace Panwar.Api.Models.DTOs;

// ---- Senders ----

public sealed record EdmSenderDto(
    Guid Id,
    string Name,
    string FromAddress,
    string? ReplyTo,
    string BrandColour,
    string? LogoUrl,
    string FooterText);

public class EdmSenderWriteRequest
{
    public string Name { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string? ReplyTo { get; set; }
    public string BrandColour { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string FooterText { get; set; } = "";
}

// ---- Lists ----

public sealed record EdmListDto(
    Guid Id,
    string Name,
    string Kind,
    string? SyncSource,
    EdmSenderDto Sender,
    int Subscribed,
    int Unsubscribed,
    int Bounced,
    DateTime? LastSyncedAt,
    string? LastSyncError,
    DateTime CreatedAt);

public class EdmListWriteRequest
{
    public string Name { get; set; } = "";
    public Guid SenderId { get; set; }
    // Only read on create: null for a custom (CSV) list, "PharmaChat" / "ClinicalStudio" for a synced one.
    public string? SyncSource { get; set; }
}

public sealed record EdmContactDto(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    string Status,
    DateTime? StatusChangedAt,
    DateTime CreatedAt);

public sealed record EdmContactPage(IReadOnlyList<EdmContactDto> Contacts, int Total, int Page, int PageSize);

public class EdmContactWriteRequest
{
    public string Email { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class EdmContactStatusRequest
{
    // "subscribed" or "unsubscribed" — bounces only come from delivery reports.
    public string Status { get; set; } = "";
}

public class EdmImportRequest
{
    public List<EdmContactWriteRequest> Contacts { get; set; } = new();
    // "add" keeps everyone already on the list; "replace" removes subscribed contacts missing from the file.
    public string Mode { get; set; } = "add";
}

public sealed record EdmImportResult(
    int Added,
    int Updated,
    int Removed,
    int Suppressed,
    int Invalid,
    IReadOnlyList<string> InvalidSamples);

public sealed record EdmSyncResult(int Added, int Updated, int Unsubscribed, int WrittenBack, string? Error);

// ---- Campaigns ----

public sealed record EdmCampaignStats(
    int Total,
    int Pending,
    int Sent,
    int Delivered,
    int Bounced,
    int Failed,
    int Opened,
    int Unsubscribed);

public sealed record EdmCampaignSummaryDto(
    Guid Id,
    string Name,
    string? CampaignCode,
    string Status,
    string? ListName,
    int? ListSize,
    DateTime? ScheduledFor,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime UpdatedAt,
    string CreatedByName,
    EdmCampaignStats Stats);

public sealed record EdmContentAnalysis(
    int HtmlBytes,
    int LinkCount,
    int ImageCount,
    bool HasTextVersion,
    bool HasUnsubscribeTag,
    IReadOnlyList<string> LinksMissingUtm,
    IReadOnlyList<string> MissingImages,
    IReadOnlyList<string> Warnings);

public sealed record EdmCampaignDto(
    Guid Id,
    string Name,
    string? CampaignCode,
    string Status,
    Guid? ListId,
    string? ListName,
    Guid? SenderId,
    EdmSenderDto? Sender,
    string? Subject,
    string? PreviewText,
    string? SourceFileName,
    EdmContentAnalysis? Content,
    DateTime? ScheduledFor,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    string CreatedByName,
    string? SentByName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    EdmCampaignStats Stats);

public class EdmCampaignCreateRequest
{
    public string? Name { get; set; }
    public Guid? ListId { get; set; }
}

public class EdmCampaignUpdateRequest
{
    public string? Name { get; set; }
    public string? CampaignCode { get; set; }
    public Guid? ListId { get; set; }
    public Guid? SenderId { get; set; }
    public string? Subject { get; set; }
    public string? PreviewText { get; set; }
}

public sealed record EdmContentUploadResult(
    EdmCampaignDto Campaign,
    string? DetectedSubject,
    string? DetectedPreviewText);

public sealed record EdmPreviewRecipient(Guid? ContactId, string Email, string? FirstName, string? LastName);

public sealed record EdmPreviewDto(string Html, string Subject, EdmPreviewRecipient Recipient, IReadOnlyList<EdmPreviewRecipient> Samples);

public sealed record EdmAudienceDto(string ListName, int ListTotal, int Sendable, int Skipped);

public class EdmTestSendRequest
{
    public List<string>? Emails { get; set; }
}

public sealed record EdmTestSendResult(IReadOnlyList<string> SentTo);

public class EdmSendRequest
{
    // Sydney wall-clock time, "yyyy-MM-ddTHH:mm". Null sends now.
    public string? ScheduleAt { get; set; }
    // The recipient count the user typed to confirm; must match the live sendable count.
    public int ConfirmCount { get; set; }
}

public sealed record EdmReportRow(string Email, string? FirstName, string? LastName, string Status, DateTime? At, string? Detail);

public sealed record EdmReportDto(
    EdmCampaignDto Campaign,
    IReadOnlyList<EdmReportRow> Bounces,
    IReadOnlyList<EdmReportRow> Unsubscribes,
    IReadOnlyList<EdmReportRow> Failures);
