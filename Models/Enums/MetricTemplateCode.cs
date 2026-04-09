namespace Panwar.Api.Models.Enums;

/// <summary>
/// The 6 metric template shapes observed in the Reckitt workbook.
/// Each placement is typed by exactly one of these, which determines
/// the set of metric_field rows that apply to it.
/// </summary>
public enum MetricTemplateCode
{
    DigitalDisplay = 0,
    Edm = 1,
    Print = 2,
    SponsoredContent = 3,
    EducationVideo = 4,
    EducationCourse = 5
}
