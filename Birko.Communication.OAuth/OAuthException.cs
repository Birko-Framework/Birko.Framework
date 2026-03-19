using System;

namespace Birko.Communication.OAuth;

/// <summary>
/// Exception thrown when an OAuth2 operation fails.
/// </summary>
public class OAuthException : Exception
{
    /// <summary>
    /// The OAuth2 error code (e.g., "invalid_grant", "invalid_client").
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// The OAuth2 error description from the server.
    /// </summary>
    public string? ErrorDescription { get; }

    /// <summary>
    /// The HTTP status code of the response, if applicable.
    /// </summary>
    public int? StatusCode { get; }

    public OAuthException(string message) : base(message)
    {
    }

    public OAuthException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public OAuthException(string message, string? errorCode, string? errorDescription, int? statusCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
        ErrorDescription = errorDescription;
        StatusCode = statusCode;
    }

    public OAuthException(string message, string? errorCode, string? errorDescription, int? statusCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ErrorDescription = errorDescription;
        StatusCode = statusCode;
    }
}
