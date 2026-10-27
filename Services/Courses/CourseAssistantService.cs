using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services.Ai;

namespace Panwar.Api.Services.Courses;

public interface ICourseAssistantService
{
    Task<JsonNode?> TakeTurnAsync(Guid courseId, CourseAssistantRequest request, CancellationToken cancellationToken = default);
}

public class CourseAssistantService : ICourseAssistantService
{
    private readonly AppDbContext _context;
    private readonly IAnthropicService _anthropic;
    private readonly string _model;
    private readonly string _effort;

    public CourseAssistantService(AppDbContext context, IAnthropicService anthropic, IConfiguration configuration)
    {
        _context = context;
        _anthropic = anthropic;
        _model = configuration["COURSE_AI_MODEL"] ?? "claude-opus-5-5";
        _effort = configuration["COURSE_AI_EFFORT"] ?? "high";
    }

    public async Task<JsonNode?> TakeTurnAsync(Guid courseId, CourseAssistantRequest request, CancellationToken cancellationToken = default)
    {
        if (!_anthropic.IsEnabled) throw new CourseValidationException("The AI assistant isn't set up on this server");
        if (!await _context.Courses.AnyAsync(c => c.Id == courseId, cancellationToken)) return null;

        if (request.System.ValueKind is not (JsonValueKind.String or JsonValueKind.Array))
            throw new CourseValidationException("system is required");
        if (request.Tools.ValueKind != JsonValueKind.Array) throw new CourseValidationException("tools must be a list");
        if (request.Messages.ValueKind != JsonValueKind.Array || request.Messages.GetArrayLength() == 0)
            throw new CourseValidationException("messages are required");

        try
        {
            return await _anthropic.CreateMessageAsync(new MessageTurnRequest(
                _model,
                _effort,
                JsonNode.Parse(request.System.GetRawText())!,
                JsonNode.Parse(request.Tools.GetRawText())!.AsArray(),
                JsonNode.Parse(request.Messages.GetRawText())!.AsArray()), cancellationToken);
        }
        catch (AnthropicException ex)
        {
            throw new CourseValidationException($"The AI request failed: {ex.Message}");
        }
    }
}
