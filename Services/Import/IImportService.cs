using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Import;

public interface IImportService
{
    Task<ImportPreviewDto> BuildPreviewAsync(string clientSlug, ImportPreviewRequest request, CancellationToken ct);
    Task<ImportCommitResultDto> CommitAsync(string clientSlug, ImportCommitRequest request, Guid? userId, CancellationToken ct);
}
