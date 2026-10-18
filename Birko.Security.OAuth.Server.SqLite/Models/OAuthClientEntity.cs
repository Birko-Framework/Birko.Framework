using System;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;

namespace Birko.Security.OAuth.Server.SqLite.Models;

/// <summary>
/// Row of <c>oauth_clients</c>, the stored form of <see cref="Server.Models.OAuthClient"/>. The SQL column mapper has
/// no mapping for its three <c>List&lt;string&gt;</c> properties (it refuses the type at table load), so they are
/// stored together as JSON in <see cref="CollectionsJson"/>. Column names follow DraCode's original store, so an
/// existing <c>oauth_clients</c> table is read as is.
/// </summary>
[Table("oauth_clients")]
public sealed class OAuthClientEntity : AbstractDatabaseLogModel
{
    [RequiredField]
    [MaxLengthField(200)]
    [IndexedField("ix_oauth_clients_client_id")]
    public string ClientId { get; set; } = string.Empty;

    [MaxLengthField(128)]
    public string? ClientSecretHash { get; set; }

    /// <summary><see cref="Server.Models.OAuthClientType"/> as its integer value.</summary>
    public int ClientType { get; set; }

    [MaxLengthField(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTime ClientCreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>JSON of <c>{ redirectUris, allowedGrantTypes, allowedScopes }</c>.</summary>
    public string CollectionsJson { get; set; } = "{}";

    public override AbstractModel CopyTo(AbstractModel? clone = null)
    {
        var target = clone as OAuthClientEntity ?? new OAuthClientEntity();
        base.CopyTo(target);
        target.ClientId = ClientId;
        target.ClientSecretHash = ClientSecretHash;
        target.ClientType = ClientType;
        target.Name = Name;
        target.IsEnabled = IsEnabled;
        target.ClientCreatedAt = ClientCreatedAt;
        target.CollectionsJson = CollectionsJson;
        return target;
    }
}
