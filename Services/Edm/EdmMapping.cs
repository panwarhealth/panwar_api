using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Edm;

internal static class EdmMapping
{
    public static EdmSenderDto ToDto(EdmSender s) =>
        new(s.Id, s.Name, s.FromAddress, s.ReplyTo, s.BrandColour, s.LogoUrl, s.FooterText);

    public static EdmContactDto ToDto(EdmContact c) =>
        new(c.Id, c.Email, c.FirstName, c.LastName, c.Status.ToString(), c.StatusChangedAt, c.CreatedAt);
}
