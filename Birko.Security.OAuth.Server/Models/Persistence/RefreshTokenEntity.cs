using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Birko.Data.Models;

namespace Birko.Security.OAuth.Server.Models.Persistence;

/// <summary>
/// The stored form of <see cref="RefreshTokenRecord"/> (TASK-1013), the same on every backend: the record's
/// <c>Claims</c> (the extra access-token claims every refresh re-issues, TASK-517) as JSON in <see cref="ClaimsJson"/>,
/// every other member under its own name.
/// </summary>
/// <remarks>
/// Table and column names are those of the deleted <c>Birko.Security.OAuth.Server.SqLite</c>'s entity of the same name
/// (<c>oauth_refresh_tokens</c>), so a database written by that package is read as is. DataAnnotations attributes, as on
/// <see cref="OAuthClientEntity"/>: no index is declared, an existing table keeps its own.
/// </remarks>
[Table("oauth_refresh_tokens")]
public sealed class RefreshTokenEntity : AbstractModel
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    [Key]
    public override Guid? Guid { get; set; }

    [Required]
    [MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ClientId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string UserId { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool Revoked { get; set; }

    /// <summary>JSON object of the extra access-token claims; <c>{}</c> when there are none.</summary>
    public string ClaimsJson { get; set; } = "{}";

    /// <summary>Writes <paramref name="record"/>'s members onto this entity; the Guid is left as it is.</summary>
    public void CopyFrom(RefreshTokenRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        TokenHash = record.TokenHash;
        ClientId = record.ClientId;
        UserId = record.UserId;
        Scope = record.Scope;
        ExpiresAt = record.ExpiresAt;
        Revoked = record.Revoked;
        ClaimsJson = JsonSerializer.Serialize(record.Claims ?? new Dictionary<string, string>(), JsonOptions);
    }

    /// <summary>The record this entity stores.</summary>
    public RefreshTokenRecord ToModel() => new()
    {
        Guid = Guid,
        TokenHash = TokenHash,
        ClientId = ClientId,
        UserId = UserId,
        Scope = Scope,
        ExpiresAt = ExpiresAt,
        Revoked = Revoked,
        Claims = JsonSerializer.Deserialize<Dictionary<string, string>>(string.IsNullOrEmpty(ClaimsJson) ? "{}" : ClaimsJson, JsonOptions) ?? new(),
    };

    public override AbstractModel CopyTo(AbstractModel? clone = null)
    {
        var target = clone as RefreshTokenEntity ?? new RefreshTokenEntity();
        base.CopyTo(target);
        target.TokenHash = TokenHash;
        target.ClientId = ClientId;
        target.UserId = UserId;
        target.Scope = Scope;
        target.ExpiresAt = ExpiresAt;
        target.Revoked = Revoked;
        target.ClaimsJson = ClaimsJson;
        return target;
    }
}
