namespace Panwar.Api.Models.Enums;

/// <summary>
/// The kind of education activity a placement represents. Education placements
/// run over a date range (which may span multiple reporting years).
/// </summary>
public enum EducationSubcategory
{
    Module = 0,
    Article = 1,
    PodcastWebinar = 2,
    ClinicalAudit = 3,
    ResearchPaper = 4,
    Quiz = 5
}
