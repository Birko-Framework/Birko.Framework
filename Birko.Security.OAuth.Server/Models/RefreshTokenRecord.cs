using System;
using Birko.Data.Models;

namespace Birko.Security.OAuth.Server.Models;

/// <summary>
/// A refresh token issued alongside an access token. The presented token is hashed
/// before lookup so the store never holds plaintext.
/// </summary>
public class RefreshTokenRecord : AbstractModel
{
    /// <summary>SHA-256 hash of the refresh token (hex, uppercase).</summary>
    public string TokenHash { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set to <c>true</c> on rotation, logout, or compromise.</summary>
    public bool Revoked { get; set; }
}
