using System.Text.Json;

namespace Panwar.Api.Models.DTOs;

public record CourseWriteRequest(string Title, JsonElement Content);

public record CourseSummaryDto(Guid Id, string Title, DateTime UpdatedAt, string? UpdatedByName, DateTime? LastExportedAt);

public record CourseDto(Guid Id, string Title, JsonElement Content, DateTime UpdatedAt, DateTime? LastExportedAt);

public record CourseMediaDto(string Url);

public record CourseAssistantRequest(JsonElement System, JsonElement Tools, JsonElement Messages);

public record CourseMediaImportRequest(string Url);
