using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Birko.Data.Models;

namespace Birko.Security.OAuth.Server.Models.Persistence;

/// <summary>
/// The stored form of <see cref="OAuthClient"/> (TASK-1013), the same on every backend: the scalar members as they are,
/// and the three lists together as JSON in <see cref="CollectionsJson"/>. A SQL column mapper has no mapping for a
/// <c>List&lt;string&gt;</c>, and one shape everywhere keeps SQL and document stores alike.
/// </summary>
/// <remarks>
/// The table and column names are those of <c>Birko.Security.OAuth.Server.SqLite</c>'s entity of the same name
/// (<c>oauth_clients</c>, DraCode's original column names), so a database written by that package is read as is. The
/// attributes are <c>System.ComponentModel.DataAnnotations</c> ones, which the SQL mapper reads too, so this package does
/// not depend on Birko.Data.SQL. They carry no index: an existing table keeps the indexes it has.
/// <para>A separate namespace from the SQLite package's <c>Models</c>, so a consumer importing both sees no clash.</para>
/// </remarks>
[Table("oauth_clients")]
public sealed class OAuthClientEntity : AbstractLogModel
{
    private sealed record Collections(List<string>? RedirectUris, List<string>? AllowedGrantTypes, List<string>? AllowedScopes);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Key]
    public override Guid? Guid { get; set; }

    public override DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public override DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public override DateTime? PrevUpdatedAt { get; set; }

    [Required]
    [MaxLength(200)]
    public string ClientId { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? ClientSecretHash { get; set; }

    /// <summary><see cref="OAuthClientType"/> as its integer value.</summary>
    public int ClientType { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary><see cref="OAuthClient.CreatedAt"/>; the entity's own <see cref="CreatedAt"/> is the row's audit time.</summary>
    public DateTime ClientCreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>JSON of <c>{ redirectUris, allowedGrantTypes, allowedScopes }</c>.</summary>
    public string CollectionsJson { get; set; } = "{}";

    /// <summary>Writes <paramref name="client"/>'s members onto this entity; the Guid is left as it is.</summary>
    public void CopyFrom(OAuthClient client)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));
        ClientId = client.ClientId;
        ClientSecretHash = client.ClientSecretHash;
        ClientType = (int)client.ClientType;
        Name = client.Name;
        IsEnabled = client.IsEnabled;
        ClientCreatedAt = client.CreatedAt;
        CollectionsJson = JsonSerializer.Serialize(
            new Collections(client.RedirectUris, client.AllowedGrantTypes, client.AllowedScopes), JsonOptions);
    }

    /// <summary>The model this entity stores.</summary>
    public OAuthClient ToModel()
    {
        var collections = JsonSerializer.Deserialize<Collections>(string.IsNullOrEmpty(CollectionsJson) ? "{}" : CollectionsJson, JsonOptions);
        return new OAuthClient
        {
            Guid = Guid,
            ClientId = ClientId,
            ClientSecretHash = ClientSecretHash,
            ClientType = (OAuthClientType)ClientType,
            Name = Name,
            IsEnabled = IsEnabled,
            CreatedAt = ClientCreatedAt,
            RedirectUris = collections?.RedirectUris ?? new(),
            AllowedGrantTypes = collections?.AllowedGrantTypes ?? new(),
            AllowedScopes = collections?.AllowedScopes ?? new(),
        };
    }

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
