using System;
using Birko.Data.Models;

namespace Birko.Security.OAuth.Server.Models;

/// <summary>
/// Records that a user has already consented to a given (client, scopes) pair.
/// Lets the authorization endpoint skip the consent UI on subsequent requests.
/// </summary>
public class ConsentRecord : AbstractModel
{
    public string UserId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Space-separated scopes the user has granted.</summary>
    public string Scope { get; set; } = string.Empty;

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}
