using System.Net.Mail;

namespace Panwar.Api.Services.Edm;

/// <summary>Input clean-up shared by the eDM services.</summary>
public static class EdmText
{
    /// <summary>Lower-cased address, or null if it isn't a plausible single email address.</summary>
    public static string? NormaliseEmail(string? raw)
    {
        var e = raw?.Trim().Trim('<', '>', '"', '\'').ToLowerInvariant();
        if (string.IsNullOrEmpty(e) || e.Length > 254 || e.Contains(' ')) return null;
        if (!MailAddress.TryCreate(e, out var parsed) || parsed.Address != e) return null;
        var at = e.LastIndexOf('@');
        return at > 0 && e.IndexOf('.', at) > at + 1 && !e.EndsWith('.') ? e : null;
    }

    /// <summary>Trimmed and cut to length; null when blank.</summary>
    public static string? Optional(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length > max ? v[..max] : v;
    }

    /// <summary>Trimmed; throws when blank or too long, naming the field.</summary>
    public static string Required(string? value, string field, int max)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0) throw new EdmValidationException($"{field} is required");
        if (v.Length > max) throw new EdmValidationException($"{field} must be {max} characters or fewer");
        return v;
    }
}
