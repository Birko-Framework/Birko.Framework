namespace Birko.Security.OAuth.Server.Endpoints.Token;

/// <summary>
/// Successful /token response per RFC 6749 §5.1. The host is responsible for
/// serializing this as <c>application/json; charset=UTF-8</c> with the wire
/// names <c>access_token</c>, <c>token_type</c>, <c>expires_in</c>, <c>refresh_token</c>, <c>scope</c>.
/// </summary>
public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public string? RefreshToken { get; set; }
    public string? Scope { get; set; }
}
