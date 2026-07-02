using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Import;

public interface IImportService
{
    // Parses the files AND runs AI reconciliation on flagged rows inline, so the
    // returned preview already contains every suggestion (no second call).
    Task<ImportPreviewDto> BuildPreviewAsync(string clientSlug, ImportPreviewRequest request, Guid? userId, CancellationToken ct);
    Task<ImportCommitResultDto> CommitAsync(string clientSlug, ImportCommitRequest request, Guid? userId, CancellationToken ct);
}
