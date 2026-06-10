namespace Panwar.Api.Models.Enums;

/// <summary>
/// What an eDM placement's asset actually is. Mutually exclusive:
/// - Solus: the whole email is the client's (a dedicated send).
/// - SponsoredContent: the client's content sits inside the publisher's newsletter.
/// - Banner: the client's banner rides on a publisher-owned eDM.
/// </summary>
public enum EdmSubcategory
{
    Solus = 0,
    SponsoredContent = 1,
    Banner = 2
}
