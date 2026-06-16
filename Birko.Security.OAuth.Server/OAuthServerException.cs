using System;

namespace Birko.Security.OAuth.Server;

/// <summary>
/// Exception thrown when an OAuth server operation fails with a well-formed error response.
/// Carries the standard <c>error</c> / <c>error_description</c> pair so the host can serialize
/// it as RFC 6749 §5.2 JSON without further mapping.
/// </summary>
public class OAuthServerException : Exception
{
    /// <summary>The OAuth2 error code (see <see cref="OAuthErrorCodes"/>).</summary>
    public string ErrorCode { get; }

    /// <summary>Human-readable description of the error.</summary>
    public string? ErrorDescription { get; }

    /// <summary>Optional URI to a page describing the error.</summary>
    public string? ErrorUri { get; }

    public OAuthServerException(string errorCode, string? errorDescription = null, string? errorUri = null)
        : base(errorDescription ?? errorCode)
    {
        ErrorCode = errorCode;
        ErrorDescription = errorDescription;
        ErrorUri = errorUri;
    }
}
