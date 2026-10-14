namespace Birko.Security.Grants.AspNetCore;

/// <summary>Settings of the HTTP layer over <c>Birko.Security.Grants</c>.</summary>
public class GrantsAspNetCoreOptions
{
    /// <summary>
    /// The action that means "may see this resource". On a denial against a <see cref="ResourceVisibility.Granted"/>
    /// type, a caller who may perform it gets 403 and anyone else 404 (STORY-062 decision 2b). Must be declared in the
    /// role table; checked when the first <c>RequireGrant</c> endpoint is built. Default <c>read</c>.
    /// </summary>
    public string ReadAction { get; set; } = "read";
}
