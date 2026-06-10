using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services;

/// <summary>
/// snake_case string ↔ enum conversions for the placement sub-category enums,
/// shared by the dashboard read services and the admin write function.
/// </summary>
internal static class PlacementEnumNames
{
    public static string ToName(EdmSubcategory v) => v switch
    {
        EdmSubcategory.Solus => "solus",
        EdmSubcategory.SponsoredContent => "sponsored_content",
        EdmSubcategory.Banner => "banner",
        _ => v.ToString().ToLowerInvariant(),
    };

    public static string ToName(EducationSubcategory v) => v switch
    {
        EducationSubcategory.Module => "module",
        EducationSubcategory.Article => "article",
        EducationSubcategory.PodcastWebinar => "podcast_webinar",
        EducationSubcategory.ClinicalAudit => "clinical_audit",
        EducationSubcategory.ResearchPaper => "research_paper",
        EducationSubcategory.Quiz => "quiz",
        _ => v.ToString().ToLowerInvariant(),
    };

    public static bool TryParseEdm(string? name, out EdmSubcategory value)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "solus": value = EdmSubcategory.Solus; return true;
            case "sponsored_content": value = EdmSubcategory.SponsoredContent; return true;
            case "banner": value = EdmSubcategory.Banner; return true;
            default: value = default; return false;
        }
    }

    public static bool TryParseEducation(string? name, out EducationSubcategory value)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "module": value = EducationSubcategory.Module; return true;
            case "article": value = EducationSubcategory.Article; return true;
            case "podcast_webinar": value = EducationSubcategory.PodcastWebinar; return true;
            case "clinical_audit": value = EducationSubcategory.ClinicalAudit; return true;
            case "research_paper": value = EducationSubcategory.ResearchPaper; return true;
            case "quiz": value = EducationSubcategory.Quiz; return true;
            default: value = default; return false;
        }
    }
}
