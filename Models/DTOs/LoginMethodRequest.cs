namespace Panwar.Api.Models.DTOs;

public class LoginMethodRequest
{
    public string Email { get; set; } = "";
}

public class LoginMethodResponse
{
    /// <summary>"magic-link", "entra", or "denied"</summary>
    public string Method { get; set; } = "";
}
