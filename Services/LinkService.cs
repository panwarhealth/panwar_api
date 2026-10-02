using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

public class LinkService : ILinkService
{
    private const int MaxUrlLength = 2000;
    private const int MaxCampaignLength = 40;
    private const int MaxTagLength = 50;
    private const int MinQrSize = 128;
    private const int MaxQrSize = 4096;

    private static readonly Regex NonSlugChars = new("[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex JobNumber = new("^([a-z]{2,5})[0-9]{4,5}$", RegexOptions.Compiled);
    private static readonly Regex HexColour = new("^#[0-9a-f]{6}$", RegexOptions.Compiled);
    private static readonly string[] UtmKeys = ["utm_source", "utm_medium", "utm_campaign", "utm_content"];

    private readonly AppDbContext _context;
    private readonly IJobClientService _jobClients;

    public LinkService(AppDbContext context, IJobClientService jobClients)
    {
        _context = context;
        _jobClients = jobClients;
    }

    public async Task<IReadOnlyList<JobClientDto>> ListJobClientsAsync(CancellationToken cancellationToken = default)
    {
        var clients = await _jobClients.GetClientsByPrefixAsync(cancellationToken);
        return clients.OrderBy(c => c.Key).Select(c => new JobClientDto(c.Key, c.Value)).ToList();
    }

    public async Task<IReadOnlyList<TrackedLinkDto>> ListLinksAsync(CancellationToken cancellationToken = default)
    {
        return await _context.TrackedLinks.AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new TrackedLinkDto(
                l.Id, l.DestinationUrl, l.CampaignId, l.ClientName, l.Source, l.Medium, l.Content, l.Url,
                l.Creator.Name ?? l.Creator.Email, l.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<TrackedLinkDto> CreateLinkAsync(TrackedLinkWriteRequest data, Guid userId, CancellationToken cancellationToken = default)
    {
        var destination = ParseWebAddress(data.DestinationUrl);

        var campaign = Slugify(data.CampaignId);
        if (campaign.Length < 3 || campaign.Length > MaxCampaignLength)
            throw new LinkValidationException($"Campaign id must be 3 to {MaxCampaignLength} characters");

        var source = RequireTag(data.Source, "Source");
        var medium = RequireTag(data.Medium, "Medium");

        var job = JobNumber.Match(campaign);
        string? clientName = null;
        if (job.Success)
        {
            var clients = await _jobClients.GetClientsByPrefixAsync(cancellationToken);
            clients.TryGetValue(job.Groups[1].Value, out clientName);
        }

        var now = DateTime.UtcNow;
        var link = new TrackedLink
        {
            Id = Guid.NewGuid(),
            DestinationUrl = destination.AbsoluteUri,
            CampaignId = job.Success ? campaign.ToUpperInvariant() : campaign,
            ClientName = clientName,
            Source = source,
            Medium = medium,
            Url = BuildUrl(destination, source, medium, campaign, null),
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.TrackedLinks.Add(link);
        await _context.SaveChangesAsync(cancellationToken);

        return await LoadLinkAsync(link.Id, cancellationToken);
    }

    public async Task<TrackedLinkDto?> SetContentAsync(Guid linkId, string? content, CancellationToken cancellationToken = default)
    {
        var link = await _context.TrackedLinks.FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null) return null;

        var tag = Slugify(content ?? "");
        if (tag.Length > MaxTagLength)
            throw new LinkValidationException($"Tag must be {MaxTagLength} characters or fewer");

        link.Content = tag.Length == 0 ? null : tag;
        link.Url = BuildUrl(new Uri(link.DestinationUrl), link.Source, link.Medium, link.CampaignId.ToLowerInvariant(), link.Content);
        link.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return await LoadLinkAsync(link.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<QrCodeDto>> ListQrCodesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.QrCodes.AsNoTracking()
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => new QrCodeDto(
                q.Id, q.Url, q.Size, q.Foreground, q.Background, q.HasLogo,
                q.Creator.Name ?? q.Creator.Email, q.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<QrCodeDto> SaveQrCodeAsync(QrCodeWriteRequest data, Guid userId, CancellationToken cancellationToken = default)
    {
        var url = data.Url.Trim();
        ParseWebAddress(url);
        var foreground = RequireColour(data.Foreground, "Foreground");
        var background = RequireColour(data.Background, "Background");
        if (data.Size < MinQrSize || data.Size > MaxQrSize)
            throw new LinkValidationException($"Size must be {MinQrSize} to {MaxQrSize} pixels");

        var existing = await _context.QrCodes.FirstOrDefaultAsync(q =>
            q.Url == url && q.Size == data.Size && q.Foreground == foreground
            && q.Background == background && q.HasLogo == data.HasLogo, cancellationToken);

        if (existing is null)
        {
            existing = new QrCode
            {
                Id = Guid.NewGuid(),
                Url = url,
                Size = data.Size,
                Foreground = foreground,
                Background = background,
                HasLogo = data.HasLogo,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
            };
            _context.QrCodes.Add(existing);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var id = existing.Id;
        return await _context.QrCodes.AsNoTracking()
            .Where(q => q.Id == id)
            .Select(q => new QrCodeDto(
                q.Id, q.Url, q.Size, q.Foreground, q.Background, q.HasLogo,
                q.Creator.Name ?? q.Creator.Email, q.CreatedAt))
            .FirstAsync(cancellationToken);
    }

    private async Task<TrackedLinkDto> LoadLinkAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.TrackedLinks.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new TrackedLinkDto(
                l.Id, l.DestinationUrl, l.CampaignId, l.ClientName, l.Source, l.Medium, l.Content, l.Url,
                l.Creator.Name ?? l.Creator.Email, l.CreatedAt))
            .FirstAsync(cancellationToken);
    }

    private static Uri ParseWebAddress(string? value)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length > MaxUrlLength)
            throw new LinkValidationException($"Web address must be {MaxUrlLength} characters or fewer");
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new LinkValidationException("That doesn't look like a web address");
        return uri;
    }

    private static string RequireTag(string? value, string label)
    {
        var tag = Slugify(value ?? "");
        if (tag.Length == 0 || tag.Length > MaxTagLength)
            throw new LinkValidationException($"{label} must be 1 to {MaxTagLength} characters");
        return tag;
    }

    private static string RequireColour(string? value, string label)
    {
        var colour = (value ?? "").Trim().ToLowerInvariant();
        if (!HexColour.IsMatch(colour))
            throw new LinkValidationException($"{label} must be a hex colour like #702f8f");
        return colour;
    }

    private static string Slugify(string value)
        => NonSlugChars.Replace(value.Trim().ToLowerInvariant().Replace("&", " and "), "-").Trim('-');

    private static string BuildUrl(Uri destination, string source, string medium, string campaign, string? content)
    {
        var parts = destination.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !UtmKeys.Contains(p.Split('=')[0], StringComparer.OrdinalIgnoreCase))
            .ToList();

        parts.Add($"utm_source={source}");
        parts.Add($"utm_medium={medium}");
        parts.Add($"utm_campaign={campaign}");
        if (content is not null) parts.Add($"utm_content={content}");

        return new UriBuilder(destination) { Query = string.Join('&', parts) }.Uri.AbsoluteUri;
    }
}
