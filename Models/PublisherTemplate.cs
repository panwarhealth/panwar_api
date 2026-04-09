namespace Panwar.Api.Models;

/// <summary>
/// Many-to-many between Publisher and MetricTemplate. AJP, for example,
/// offers digital_display, edm, print, and education_course templates.
/// </summary>
public class PublisherTemplate
{
    public Guid PublisherId { get; set; }
    public Guid TemplateId { get; set; }

    public Publisher Publisher { get; set; } = null!;
    public MetricTemplate Template { get; set; } = null!;
}
