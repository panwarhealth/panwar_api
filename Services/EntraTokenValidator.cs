using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Panwar.Api.Services;

public class EntraTokenValidator : IEntraTokenValidator
{
    private readonly ILogger<EntraTokenValidator> _logger;
    private readonly string _clientId;
    private readonly string _tenantId;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager;

    public EntraTokenValidator(IConfiguration configuration, ILogger<EntraTokenValidator> logger)
    {
        _logger = logger;

        _tenantId = configuration["ENTRA_TENANT_ID"]
            ?? throw new InvalidOperationException("ENTRA_TENANT_ID not configured");

        _clientId = configuration["ENTRA_EMPLOYEE_SSO_CLIENT_ID"]
            ?? throw new InvalidOperationException("ENTRA_EMPLOYEE_SSO_CLIENT_ID not configured");

        var metadataUrl = $"https://login.microsoftonline.com/{_tenantId}/v2.0/.well-known/openid-configuration";

        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataUrl,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());
    }

    public async Task<EntraTokenResult?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var openIdConfig = await _configManager.GetConfigurationAsync(cancellationToken);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = openIdConfig.SigningKeys,
                ValidateIssuer = true,
                ValidIssuer = $"https://login.microsoftonline.com/{_tenantId}/v2.0",
                ValidateAudience = true,
                ValidAudience = _clientId,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var handler = new JwtSecurityTokenHandler();
            handler.MapInboundClaims = false; // preserve original JWT claim names (oid, roles, etc.)
            var principal = handler.ValidateToken(idToken, validationParameters, out _);

            var claimTypes = principal.Claims.Select(c => $"{c.Type}={c.Value}").ToList();
            _logger.LogInformation("Entra token claims: {Claims}", string.Join(" | ", claimTypes));

            var oid = principal.FindFirstValue("oid")
                ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");

            var email = principal.FindFirstValue("email")
                ?? principal.FindFirstValue("preferred_username")
                ?? principal.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrWhiteSpace(oid) || string.IsNullOrWhiteSpace(email))
            {
                _logger.LogWarning("Entra token valid but missing oid or email claim");
                return null;
            }

            var givenName = principal.FindFirstValue("given_name") ?? principal.FindFirstValue(ClaimTypes.GivenName);
            var familyName = principal.FindFirstValue("family_name") ?? principal.FindFirstValue(ClaimTypes.Surname);
            var name = principal.FindFirstValue("name");

            if (string.IsNullOrWhiteSpace(name) && (!string.IsNullOrWhiteSpace(givenName) || !string.IsNullOrWhiteSpace(familyName)))
            {
                name = $"{givenName} {familyName}".Trim();
            }

            var roles = principal.FindAll("roles").Select(c => c.Value).ToArray();

            return new EntraTokenResult(oid, email, name, roles);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Entra token validation failed");
            return null;
        }
    }
}
