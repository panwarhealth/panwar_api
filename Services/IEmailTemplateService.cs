namespace Panwar.Api.Services;

public interface IEmailTemplateService
{
    string Render(
        string templateFile,
        IReadOnlyDictionary<string, string> tokens,
        IReadOnlyDictionary<string, string>? rawTokens = null);
}
