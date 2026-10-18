using System;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;

namespace Birko.Security.OAuth.Server.SqLite.Models;

/// <summary>
/// Row of <c>oauth_refresh_tokens</c>, the stored form of <see cref="Server.Models.RefreshTokenRecord"/>. The record's
/// <c>Claims</c> dictionary (TASK-517: the extra access-token claims every refresh re-issues) has no column mapping,
/// so it is stored as JSON in <see cref="ClaimsJson"/>; the other columns keep the record's names.
/// </summary>
[Table("oauth_refresh_tokens")]
public sealed class RefreshTokenEntity : AbstractModel
{
    [PrimaryField]
    [UniqueField]
    public override Guid? Guid { get; set; }

    [RequiredField]
    [MaxLengthField(128)]
    [IndexedField("ix_oauth_refresh_tokens_hash")]
    public string TokenHash { get; set; } = string.Empty;

    [MaxLengthField(200)]
    [IndexedField("ix_oauth_refresh_tokens_family", 0)]
    public string ClientId { get; set; } = string.Empty;

    [MaxLengthField(200)]
    [IndexedField("ix_oauth_refresh_tokens_family", 1)]
    public string UserId { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool Revoked { get; set; }

    /// <summary>JSON object of the extra access-token claims; <c>{}</c> when there are none.</summary>
    public string ClaimsJson { get; set; } = "{}";
}
