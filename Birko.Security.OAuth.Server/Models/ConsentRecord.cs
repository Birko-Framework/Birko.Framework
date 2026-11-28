using System;
using Birko.Data.Models;

namespace Birko.Security.OAuth.Server.Models;

/// <summary>
/// Records that a user has already consented to a given (client, scopes) pair.
/// Lets the authorization endpoint skip the consent UI on subsequent requests.
/// </summary>
/// <remarks>Stored in <c>oauth_consents</c> on a SQL backend (TASK-1013), the name <c>Birko.Security.OAuth.Server.SqLite</c> registers, so its databases stay readable.</remarks>
[System.ComponentModel.DataAnnotations.Schema.Table("oauth_consents")]
public class ConsentRecord : AbstractModel
{
    public string UserId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Space-separated scopes the user has granted.</summary>
    public string Scope { get; set; } = string.Empty;

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}
