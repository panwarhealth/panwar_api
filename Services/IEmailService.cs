namespace Panwar.Api.Services;

public interface IEmailService
{
    Task SendMagicLinkEmailAsync(string toEmail, string magicLink, CancellationToken cancellationToken = default);
}
