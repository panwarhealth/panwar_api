using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

public class LinkValidationException : Exception
{
    public LinkValidationException(string message) : base(message) { }
}

public interface ILinkService
{
    Task<IReadOnlyList<TrackedLinkDto>> ListLinksAsync(CancellationToken cancellationToken = default);
    Task<TrackedLinkDto> CreateLinkAsync(TrackedLinkWriteRequest data, Guid userId, CancellationToken cancellationToken = default);
    Task<TrackedLinkDto?> SetContentAsync(Guid linkId, string? content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobClientDto>> ListJobClientsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QrCodeDto>> ListQrCodesAsync(CancellationToken cancellationToken = default);
    Task<QrCodeDto> SaveQrCodeAsync(QrCodeWriteRequest data, Guid userId, CancellationToken cancellationToken = default);
}
