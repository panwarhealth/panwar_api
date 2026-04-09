using Panwar.Api.Models.Enums;

namespace Panwar.Api.Models;

/// <summary>
/// One of the 6 metric template shapes (DigitalDisplay, Edm, Print,
/// SponsoredContent, EducationVideo, EducationCourse). Determines which
/// metric_field rows apply to a placement of this type.
/// </summary>
public class MetricTemplate
{
    public Guid Id { get; set; }
    public MetricTemplateCode Code { get; set; }
    public required string Name { get; set; }

    public ICollection<MetricField> Fields { get; set; } = new List<MetricField>();
    public ICollection<PublisherTemplate> PublisherTemplates { get; set; } = new List<PublisherTemplate>();
    public ICollection<Placement> Placements { get; set; } = new List<Placement>();
}
