namespace Panwar.Api.Models.Enums;

/// <summary>
/// The metric template shapes. Each placement is typed by exactly one of these,
/// which determines the set of metric_field rows that apply to it. Education
/// further splits by <see cref="EducationSubcategory"/> and eDM by
/// <see cref="EdmSubcategory"/>.
/// </summary>
public enum MetricTemplateCode
{
    DigitalDisplay = 0,
    Edm = 1,
    Print = 2,
    SponsoredContent = 3,
    Education = 4
}
