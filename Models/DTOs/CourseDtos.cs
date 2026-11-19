using System.Text.Json;

namespace Panwar.Api.Models.DTOs;

public record CourseWriteRequest(string Title, JsonElement Content);

public record CourseSummaryDto(Guid Id, string Title, DateTime UpdatedAt, string? UpdatedByName, DateTime? LastExportedAt);

public record CourseVersionDto(Guid Id, DateTime SavedAt, string? SavedByName, string Title);

public record CoursePresenceRequest(string? PageId);

public record CoursePresenceDto(Guid UserId, string Name, string? PageId);

public record CourseDto(Guid Id, string Title, JsonElement Content, DateTime UpdatedAt, DateTime? LastExportedAt, Guid? ClinicalStudioCourseId);

public record CourseClinicalStudioRequest(string? CourseId);

public record CoursePublishStartRequest(string[] FilePaths);

public record CoursePublishCompleteRequest(string UploadId, int TotalFiles, string? ConfirmCourseTitle);

public record CourseMediaDto(string Url);

public record CourseAssistantRequest(JsonElement System, JsonElement Tools, JsonElement Messages);

public record CourseMediaImportRequest(string Url);
