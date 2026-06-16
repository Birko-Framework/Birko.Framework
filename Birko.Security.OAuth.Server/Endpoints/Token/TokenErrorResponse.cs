namespace Birko.Security.OAuth.Server.Endpoints.Token;

/// <summary>
/// /token error response per RFC 6749 §5.2. Field names map to the wire form
/// <c>error</c>, <c>error_description</c>, <c>error_uri</c>.
/// </summary>
public class TokenErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string? ErrorDescription { get; set; }
    public string? ErrorUri { get; set; }

    public static TokenErrorResponse From(OAuthServerException ex) => new()
    {
        Error = ex.ErrorCode,
        ErrorDescription = ex.ErrorDescription,
        ErrorUri = ex.ErrorUri
    };
}
