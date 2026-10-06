using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Edm;

/// <summary>Campaign drafts: create, edit, upload content, preview, duplicate, delete.</summary>
public interface IEdmCampaignService
{
    Task<IReadOnlyList<EdmCampaignSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<EdmCampaignDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<EdmCampaignDto> CreateAsync(EdmCampaignCreateRequest data, Guid userId, CancellationToken ct = default);
    Task<EdmCampaignDto?> UpdateAsync(Guid id, EdmCampaignUpdateRequest data, CancellationToken ct = default);
    Task<EdmContentUploadResult?> UploadContentAsync(Guid id, byte[] bytes, string fileName, CancellationToken ct = default);
    Task<EdmPreviewDto?> PreviewAsync(Guid id, Guid? contactId, CancellationToken ct = default);
    Task<EdmCampaignDto?> DuplicateAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public class EdmCampaignService : IEdmCampaignService
{
    private const string DefaultName = "Untitled campaign";
    private static readonly Regex SafeKey = new("[^a-zA-Z0-9._-]+", RegexOptions.Compiled);

    private readonly AppDbContext _context;
    private readonly EdmCampaignQueries _queries;
    private readonly IEdmImageStore _images;
    private readonly IConfiguration _configuration;

    public EdmCampaignService(AppDbContext context, EdmCampaignQueries queries, IEdmImageStore images, IConfiguration configuration)
    {
        _context = context;
        _queries = queries;
        _images = images;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<EdmCampaignSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        var campaigns = await _context.EdmCampaigns.AsNoTracking()
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.CampaignCode,
                c.Status,
                ListName = c.List != null ? c.List.Name : null,
                c.ListId,
                c.ScheduledFor,
                c.StartedAt,
                c.CompletedAt,
                c.UpdatedAt,
                CreatedByName = c.Creator.Name ?? c.Creator.Email,
            })
            .ToListAsync(ct);

        var stats = await _queries.StatsAsync(null, ct);
        var listSizes = await _context.EdmContacts.AsNoTracking()
            .Where(c => c.Status == EdmContactStatus.Subscribed)
            .GroupBy(c => c.ListId)
            .Select(g => new { ListId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ListId, g => g.Count, ct);

        return campaigns.Select(c => new EdmCampaignSummaryDto(
            c.Id, c.Name, c.CampaignCode, c.Status.ToString(), c.ListName,
            c.ListId is { } l ? listSizes.GetValueOrDefault(l) : null,
            c.ScheduledFor, c.StartedAt, c.CompletedAt, c.UpdatedAt, c.CreatedByName,
            stats.GetValueOrDefault(c.Id, EdmCampaignQueries.EmptyStats))).ToList();
    }

    public async Task<EdmCampaignDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await _queries.LoadAsync(id, tracking: false, ct);
        return campaign is null ? null : await _queries.ToDtoAsync(campaign, ct);
    }

    public async Task<EdmCampaignDto> CreateAsync(EdmCampaignCreateRequest data, Guid userId, CancellationToken ct = default)
    {
        var campaign = new EdmCampaign
        {
            Id = Guid.NewGuid(),
            Name = EdmText.Optional(data.Name, 200) ?? DefaultName,
            Status = EdmCampaignStatus.Draft,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        if (data.ListId is { } listId) await ApplyListAsync(campaign, listId, ct);

        _context.EdmCampaigns.Add(campaign);
        await _context.SaveChangesAsync(ct);
        return (await GetAsync(campaign.Id, ct))!;
    }

    public async Task<EdmCampaignDto?> UpdateAsync(Guid id, EdmCampaignUpdateRequest data, CancellationToken ct = default)
    {
        var campaign = await _queries.LoadAsync(id, tracking: true, ct);
        if (campaign is null) return null;
        EdmCampaignQueries.RequireEditable(campaign);

        // Null means "not sent"; an empty string clears the field.
        if (data.Name is not null) campaign.Name = EdmText.Optional(data.Name, 200) ?? DefaultName;
        // Kept exactly as typed: it has to match utm_campaign, and GA campaign names are case-sensitive.
        if (data.CampaignCode is not null) campaign.CampaignCode = EdmText.Optional(data.CampaignCode, 40);
        if (data.Subject is not null) campaign.Subject = EdmText.Optional(data.Subject, 250);
        if (data.PreviewText is not null) campaign.PreviewText = EdmText.Optional(data.PreviewText, 250);
        if (data.ListId is { } listId && listId != campaign.ListId) await ApplyListAsync(campaign, listId, ct);
        if (data.SenderId is { } senderId && senderId != campaign.SenderId)
        {
            campaign.Sender = await _context.EdmSenders.FirstOrDefaultAsync(s => s.Id == senderId, ct)
                ?? throw new EdmValidationException("Sender not found");
            campaign.SenderId = senderId;
        }

        campaign.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return await _queries.ToDtoAsync(campaign, ct);
    }

    public async Task<EdmContentUploadResult?> UploadContentAsync(Guid id, byte[] bytes, string fileName, CancellationToken ct = default)
    {
        var campaign = await _queries.LoadAsync(id, tracking: true, ct);
        if (campaign is null) return null;
        EdmCampaignQueries.RequireEditable(campaign);

        var unpacked = EdmContentProcessor.Unpack(bytes, fileName);

        // Upload only the images the HTML references. Keys carry a content hash so a re-upload
        // with a changed image gets a fresh URL past any CDN or inbox cache.
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EdmContentProcessor.ReferencedImages(unpacked.Html))
        {
            if (!unpacked.Images.TryGetValue(path, out var image)) continue;
            var hash = Convert.ToHexString(SHA256.HashData(image.Bytes))[..10].ToLowerInvariant();
            var key = $"edm/{campaign.Id}/{hash}-{SafeKey.Replace(Path.GetFileName(path), "-")}";
            urls[path] = await _images.PutAsync(key, image.Bytes, image.ContentType, ct);
        }

        var html = EdmContentProcessor.RewriteImages(unpacked.Html, path => urls.GetValueOrDefault(path));
        var prepared = EdmContentProcessor.Prepare(html, fileName);

        campaign.HtmlBody = prepared.Html;
        campaign.TextBody = unpacked.Text;
        campaign.SourceFileName = EdmText.Optional(Path.GetFileName(fileName), 255);
        // Fill blanks from the build; never overwrite what someone typed.
        campaign.Subject ??= EdmText.Optional(prepared.Title, 250);
        campaign.PreviewText ??= EdmText.Optional(prepared.PreviewText, 250);
        campaign.CampaignCode ??= EdmText.Optional(prepared.CampaignCode, 40);
        if (campaign.Name == DefaultName)
            campaign.Name = EdmText.Optional(Path.GetFileNameWithoutExtension(fileName).Replace('_', ' '), 200) ?? DefaultName;
        campaign.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return new EdmContentUploadResult(await _queries.ToDtoAsync(campaign, ct), prepared.Title, prepared.PreviewText);
    }

    public async Task<EdmPreviewDto?> PreviewAsync(Guid id, Guid? contactId, CancellationToken ct = default)
    {
        var campaign = await _queries.LoadAsync(id, tracking: false, ct);
        if (campaign is null) return null;
        if (campaign.HtmlBody is null || campaign.Sender is null) throw new EdmValidationException("Add the email content and a sender first");

        // A handful of real people to flick between, named ones first so the merge tags show.
        var samples = campaign.ListId is { } listId
            ? await _context.EdmContacts.AsNoTracking()
                .Where(c => c.ListId == listId && c.Status == EdmContactStatus.Subscribed)
                .OrderByDescending(c => c.FirstName != null).ThenBy(c => c.Email)
                .Take(10)
                .Select(c => new EdmPreviewRecipient(c.Id, c.Email, c.FirstName, c.LastName))
                .ToListAsync(ct)
            : [];

        var recipient = samples.FirstOrDefault(s => s.ContactId == contactId)
            ?? samples.FirstOrDefault()
            ?? new EdmPreviewRecipient(null, "someone@example.com", null, null);

        var built = EdmMessageBuilder.Build(campaign, campaign.Sender, new EdmMessageBuilder.Personalisation(
            recipient.Email, recipient.FirstName, recipient.LastName, EdmUrls.Unsubscribe(_configuration, null), null));
        return new EdmPreviewDto(built.Html, built.Subject, recipient, samples);
    }

    public async Task<EdmCampaignDto?> DuplicateAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var source = await _context.EdmCampaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (source is null) return null;

        var copy = new EdmCampaign
        {
            Id = Guid.NewGuid(),
            Name = EdmText.Optional($"{source.Name} (copy)", 200)!,
            CampaignCode = source.CampaignCode,
            ListId = source.ListId,
            SenderId = source.SenderId,
            Subject = source.Subject,
            PreviewText = source.PreviewText,
            HtmlBody = source.HtmlBody,
            TextBody = source.TextBody,
            SourceFileName = source.SourceFileName,
            Status = EdmCampaignStatus.Draft,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.EdmCampaigns.Add(copy);
        await _context.SaveChangesAsync(ct);
        return await GetAsync(copy.Id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await _context.EdmCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (campaign is null) return false;
        if (campaign.Status != EdmCampaignStatus.Draft) throw new EdmValidationException("Only drafts can be deleted");
        _context.EdmCampaigns.Remove(campaign);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    private async Task ApplyListAsync(EdmCampaign campaign, Guid listId, CancellationToken ct)
    {
        var list = await _context.EdmLists.Include(l => l.Sender).FirstOrDefaultAsync(l => l.Id == listId, ct)
            ?? throw new EdmValidationException("List not found");
        // The sender comes with the list unless someone deliberately picked a different one.
        if (campaign.SenderId is null || campaign.SenderId == campaign.List?.SenderId)
        {
            campaign.SenderId = list.SenderId;
            campaign.Sender = list.Sender;
        }
        campaign.ListId = list.Id;
        campaign.List = list;
    }
}
