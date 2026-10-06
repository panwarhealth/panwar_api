namespace Panwar.Api.Models;

/// <summary>
/// A brand we send as (PharmaChat, Clinical Studio, Panwar Health). Its domain must be verified
/// on the Azure Communication Services email resource and FromAddress added as a sender username.
/// Unsubscribes are scoped to the sender: leaving one PharmaChat list leaves every PharmaChat list.
/// </summary>
public class EdmSender
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string FromAddress { get; set; }
    public string? ReplyTo { get; set; }
    public required string BrandColour { get; set; }
    public string? LogoUrl { get; set; }
    public required string FooterText { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
