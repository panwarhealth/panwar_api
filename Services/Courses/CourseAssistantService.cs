using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Panwar.Api.Data;
using Panwar.Api.Models.DTOs;
using Panwar.Api.Services.Ai;

namespace Panwar.Api.Services.Courses;

public interface ICourseAssistantService
{
    Task<Guid?> StartTurnAsync(Guid courseId, CourseAssistantRequest request, CancellationToken cancellationToken = default);
    CourseAssistantJobState? GetTurn(Guid jobId);
}

public class CourseAssistantService : ICourseAssistantService
{
    private readonly AppDbContext _context;
    private readonly IAnthropicService _anthropic;
    private readonly ICourseAssistantJobs _jobs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _model;
    private readonly string _effort;

    public CourseAssistantService(
        AppDbContext context,
        IAnthropicService anthropic,
        ICourseAssistantJobs jobs,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _context = context;
        _anthropic = anthropic;
        _jobs = jobs;
        _scopeFactory = scopeFactory;
        _model = configuration["COURSE_AI_MODEL"] ?? "claude-opus-5-5";
        _effort = configuration["COURSE_AI_EFFORT"] ?? "high";
    }

    public async Task<Guid?> StartTurnAsync(Guid courseId, CourseAssistantRequest request, CancellationToken cancellationToken = default)
    {
        if (!_anthropic.IsEnabled) throw new CourseValidationException("The AI assistant isn't set up on this server");
        if (!await _context.Courses.AnyAsync(c => c.Id == courseId, cancellationToken)) return null;

        if (request.System.ValueKind is not (JsonValueKind.String or JsonValueKind.Array))
            throw new CourseValidationException("system is required");
        if (request.Tools.ValueKind != JsonValueKind.Array) throw new CourseValidationException("tools must be a list");
        if (request.Messages.ValueKind != JsonValueKind.Array || request.Messages.GetArrayLength() == 0)
            throw new CourseValidationException("messages are required");

        var turn = new MessageTurnRequest(
            _model,
            _effort,
            JsonNode.Parse(request.System.GetRawText())!,
            JsonNode.Parse(request.Tools.GetRawText())!.AsArray(),
            JsonNode.Parse(request.Messages.GetRawText())!.AsArray());

        return _jobs.Start(async report =>
        {
            using var scope = _scopeFactory.CreateScope();
            var anthropic = scope.ServiceProvider.GetRequiredService<IAnthropicService>();
            return await anthropic.CreateMessageAsync(turn, report, CancellationToken.None);
        });
    }

    public CourseAssistantJobState? GetTurn(Guid jobId) => _jobs.Get(jobId);
}
