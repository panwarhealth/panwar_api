using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;

namespace Panwar.Api.Services.Edm;

public sealed record EdmOutgoingMessage(
    string FromAddress,
    string? ReplyTo,
    string To,
    string Subject,
    string Html,
    string? Text,
    string? UnsubscribeUrl);

public interface IEdmMailSender
{
    /// <summary>Hands the message to ACS and returns its message id. Throws <see cref="EdmThrottledException"/> on 429.</summary>
    Task<string> SendAsync(EdmOutgoingMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Azure Communication Services Email. The display name comes from the sender username configured
/// on the ACS domain (ACS doesn't take a per-message display name), so each EdmSender's FromAddress
/// must be added there with the matching display name.
/// </summary>
public class AcsEdmMailSender : IEdmMailSender
{
    private readonly EmailClient? _client;

    public AcsEdmMailSender(IConfiguration configuration)
    {
        var connectionString = configuration["ACS_EMAIL_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(connectionString)) _client = new EmailClient(connectionString);
    }

    public async Task<string> SendAsync(EdmOutgoingMessage message, CancellationToken cancellationToken = default)
    {
        if (_client is null) throw new EdmValidationException("Email sending isn't configured (ACS_EMAIL_CONNECTION_STRING)");

        var content = new EmailContent(message.Subject) { Html = message.Html };
        if (!string.IsNullOrWhiteSpace(message.Text)) content.PlainText = message.Text;

        var email = new EmailMessage(message.FromAddress, new EmailRecipients([new EmailAddress(message.To)]), content);
        if (!string.IsNullOrWhiteSpace(message.ReplyTo)) email.ReplyTo.Add(new EmailAddress(message.ReplyTo));
        if (message.UnsubscribeUrl is not null)
        {
            // RFC 8058 one-click: Gmail and Apple Mail POST "List-Unsubscribe=One-Click" to this URL.
            email.Headers.Add("List-Unsubscribe", $"<{message.UnsubscribeUrl}>");
            email.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        try
        {
            var operation = await _client.SendAsync(WaitUntil.Started, email, cancellationToken);
            return operation.Id;
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            throw new EdmThrottledException();
        }
    }
}

public class EdmThrottledException() : Exception("ACS send rate limit reached");
