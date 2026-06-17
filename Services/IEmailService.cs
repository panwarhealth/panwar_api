namespace Panwar.Api.Services;

public interface IEmailService
{
    Task SendMagicLinkEmailAsync(string toEmail, string magicLink, CancellationToken cancellationToken = default);
    Task SendHtmlEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
