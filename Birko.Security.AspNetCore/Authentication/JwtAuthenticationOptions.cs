namespace Birko.Security.AspNetCore;

/// <summary>
/// Options for configuring Birko JWT authentication.
/// </summary>
public class JwtAuthenticationOptions
{
    /// <summary>HMAC-SHA256 secret key (required, minimum 32 characters recommended).</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>JWT issuer. Default: "Birko".</summary>
    public string Issuer { get; set; } = "Birko";

    /// <summary>JWT audience. Defaults to Issuer if not set.</summary>
    public string? Audience { get; set; }

    /// <summary>Access token expiration in minutes. Default: 60.</summary>
    public int ExpirationMinutes { get; set; } = 60;

    /// <summary>Refresh token expiration in days. Default: 7.</summary>
    public int RefreshExpirationDays { get; set; } = 7;

    /// <summary>Clock skew tolerance for token validation. Default: 1 minute.</summary>
    public int ClockSkewSeconds { get; set; } = 60;

    /// <summary>Claim name mappings. Override to use custom claim types.</summary>
    public ClaimMappingOptions Claims { get; set; } = new();

    internal string EffectiveAudience => Audience ?? Issuer;
}
