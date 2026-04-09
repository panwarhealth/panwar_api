using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Panwar.Api.Services;

/// <summary>
/// Sends transactional email through Microsoft 365 (smtp.office365.com) using
/// OAuth2 client credentials → AUTH XOAUTH2 over STARTTLS. Mirrors the
/// pharmachat_api/osteo_xchange_api EmailService pattern. The shared
/// noreply@panwarhealth.com.au mailbox + Entra app reg are reused across all
/// Panwar Health backend projects — no new mailbox or app reg required.
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _fromEmail;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fromEmail = configuration["EMAIL_FROM_ADDRESS"] ?? "noreply@panwarhealth.com.au";
        _httpClient = new HttpClient();
    }

    public async Task SendMagicLinkEmailAsync(string toEmail, string magicLink, CancellationToken cancellationToken = default)
    {
        var htmlBody = BuildMagicLinkHtml(magicLink);
        await SendEmailAsync(toEmail, "Sign in to Panwar Health Portal", htmlBody, cancellationToken);
    }

    private async Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting email send to {ToEmail}", toEmail);

            using var client = new TcpClient
            {
                ReceiveTimeout = 10000,
                SendTimeout = 10000,
                LingerState = new LingerOption(true, 0),
                NoDelay = true
            };

            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectCts.Token);

            await client.ConnectAsync("smtp.office365.com", 587, linkedCts.Token);

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true };

            await ReadLineAsync(reader, cancellationToken);

            const string ehloHost = "panwarhealth.com.au";
            await WriteLineAsync(writer, $"EHLO {ehloHost}", cancellationToken);
            string? line;
            while ((line = await ReadLineAsync(reader, cancellationToken)) != null && line.StartsWith("250-")) { }

            await WriteLineAsync(writer, "STARTTLS", cancellationToken);
            await ReadLineAsync(reader, cancellationToken);

            using var sslStream = new SslStream(stream, false);
            using var sslCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "smtp.office365.com",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, sslCts.Token);

            using var sslReader = new StreamReader(sslStream);
            using var sslWriter = new StreamWriter(sslStream) { AutoFlush = true };

            await WriteLineAsync(sslWriter, $"EHLO {ehloHost}", cancellationToken);
            while ((line = await ReadLineAsync(sslReader, cancellationToken)) != null && line.StartsWith("250-")) { }

            await WriteLineAsync(sslWriter, "AUTH XOAUTH2", cancellationToken);
            line = await ReadLineAsync(sslReader, cancellationToken);
            if (line is not null && !line.StartsWith("334"))
            {
                throw new InvalidOperationException($"Expected 334 from SMTP AUTH XOAUTH2, got: {line}");
            }

            var token = await GetAccessTokenAsync(cancellationToken);
            var xoauth2 = GenerateXOAuth2String(_fromEmail, token);
            await WriteLineAsync(sslWriter, xoauth2, cancellationToken);
            line = await ReadLineAsync(sslReader, cancellationToken);
            if (line is not null && !line.StartsWith("235"))
            {
                throw new InvalidOperationException($"SMTP authentication failed: {line}");
            }

            await WriteLineAsync(sslWriter, $"MAIL FROM:<{_fromEmail}>", cancellationToken);
            var fromResponse = await ReadLineAsync(sslReader, cancellationToken);
            if (fromResponse is not null && !fromResponse.StartsWith("250"))
            {
                throw new InvalidOperationException($"MAIL FROM failed: {fromResponse}");
            }

            await WriteLineAsync(sslWriter, $"RCPT TO:<{toEmail}>", cancellationToken);
            var toResponse = await ReadLineAsync(sslReader, cancellationToken);
            if (toResponse is not null && !toResponse.StartsWith("250"))
            {
                throw new InvalidOperationException($"RCPT TO failed: {toResponse}");
            }

            await WriteLineAsync(sslWriter, "DATA", cancellationToken);
            var dataResponse = await ReadLineAsync(sslReader, cancellationToken);
            if (dataResponse is not null && !dataResponse.StartsWith("354"))
            {
                throw new InvalidOperationException($"DATA command failed: {dataResponse}");
            }

            await WriteLineAsync(sslWriter, $"From: Panwar Health <{_fromEmail}>", cancellationToken);
            await WriteLineAsync(sslWriter, $"To: <{toEmail}>", cancellationToken);
            await WriteLineAsync(sslWriter, $"Subject: {subject}", cancellationToken);
            await WriteLineAsync(sslWriter, "Content-Type: text/html; charset=utf-8", cancellationToken);
            await WriteLineAsync(sslWriter, "", cancellationToken);
            var normalizedBody = htmlBody.Replace("\r\n", "\n").Replace("\n", "\r\n");
            await WriteLineAsync(sslWriter, normalizedBody, cancellationToken);
            await WriteLineAsync(sslWriter, ".", cancellationToken);
            await ReadLineAsync(sslReader, cancellationToken);

            await WriteLineAsync(sslWriter, "QUIT", cancellationToken);
            await ReadLineAsync(sslReader, cancellationToken);

            _logger.LogInformation("Email sent successfully to {ToEmail}", toEmail);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Email send to {ToEmail} timed out", toEmail);
            throw new TimeoutException($"Email send to {toEmail} timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {ToEmail}", toEmail);
            throw;
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var clientId = _configuration["EMAIL_CLIENT_ID"]
            ?? throw new InvalidOperationException("EMAIL_CLIENT_ID not configured");
        var clientSecret = _configuration["EMAIL_CLIENT_SECRET"]
            ?? throw new InvalidOperationException("EMAIL_CLIENT_SECRET not configured");
        var tenantId = _configuration["EMAIL_TENANT_ID"]
            ?? throw new InvalidOperationException("EMAIL_TENANT_ID not configured");

        var tokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var data = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "scope", "https://outlook.office365.com/.default" },
            { "client_id", clientId },
            { "client_secret", clientSecret }
        };

        var response = await _httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(data), cancellationToken);
        var result = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OAuth2 token request failed. Status: {Status}", response.StatusCode);
            throw new InvalidOperationException($"Failed to get access token. Status: {response.StatusCode}");
        }

        var tokenResponse = JsonSerializer.Deserialize<AccessTokenResponse>(result)
            ?? throw new InvalidOperationException("Failed to deserialize access token response");
        return tokenResponse.access_token;
    }

    private static string GenerateXOAuth2String(string email, string accessToken)
    {
        var auth = $"user={email}{(char)1}auth=Bearer {accessToken}{(char)1}{(char)1}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
    }

    private async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is not null && (line.StartsWith('5') || line.StartsWith('4')))
        {
            _logger.LogError("SMTP error: {Response}", line);
        }
        return line;
    }

    private static async Task WriteLineAsync(StreamWriter writer, string command, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(command);
        await writer.WriteAsync("\r\n");
        await writer.FlushAsync(cancellationToken);
    }

    private static string BuildMagicLinkHtml(string magicLink)
    {
        // Compiled inline HTML — Panwar Health purple (#6F2C90).
        // If you need to change the design, edit the MJML source in
        // EmailTemplates/email-magic-link.mjml and recompile.
        return $@"<!doctype html>
<html xmlns=""http://www.w3.org/1999/xhtml"">
  <head>
    <meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>Sign in to Panwar Health Portal</title>
  </head>
  <body style=""margin:0;padding:0;background-color:#ffffff;font-family:Arial, sans-serif;"">
    <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""width:600px;max-width:100%;margin:0 auto;"">
      <tr>
        <td align=""center"" style=""padding:30px 0 20px 0;"">
          <h1 style=""margin:0;color:#6F2C90;font-size:24px;font-weight:700;"">Panwar Health</h1>
        </td>
      </tr>
      <tr>
        <td align=""center"" style=""padding:0 5%;font-size:14px;line-height:24px;color:#333333;"">
          Click the button below to securely sign in to your Panwar Health Portal account:
        </td>
      </tr>
      <tr>
        <td align=""center"" style=""padding:30px;"">
          <a href=""{magicLink}"" style=""display:inline-block;background:#6F2C90;color:#ffffff;font-weight:700;font-size:14px;line-height:120%;text-decoration:none;padding:14px 32px;border-radius:6px;"">Sign In to Portal</a>
        </td>
      </tr>
      <tr>
        <td align=""center"" style=""padding:0 5% 24px 5%;font-size:13px;line-height:22px;color:#454646;"">
          <strong>Important:</strong> This link will expire in 15 minutes for security purposes. If you didn't request this sign-in link, you can safely ignore this email.
        </td>
      </tr>
      <tr>
        <td style=""padding:0 5%;""><hr style=""border:none;border-top:1px solid #e0e0e0;margin:0;""></td>
      </tr>
      <tr>
        <td style=""padding:20px 5%;font-size:12px;line-height:18px;color:#666666;"">
          <p style=""margin:0 0 8px 0;"">This is an automated message from Panwar Health. Please do not reply to this email.</p>
          <p style=""margin:0;"">&copy; {DateTime.UtcNow.Year} Panwar Health. All rights reserved.</p>
        </td>
      </tr>
    </table>
  </body>
</html>";
    }

    private sealed class AccessTokenResponse
    {
        public string access_token { get; set; } = "";
        public string token_type { get; set; } = "";
        public int expires_in { get; set; }
    }
}
