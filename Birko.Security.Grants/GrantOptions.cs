using System;
using System.Collections.Generic;

namespace Birko.Security.Grants;

/// <summary>
/// Declares the roles, the actions each role allows, and the resource types grants may be written for.
/// Nothing here is usable until <see cref="Build"/> validates it into an immutable <see cref="GrantRoleTable"/>.
/// </summary>
/// <example>
/// <code>
/// var table = new GrantOptions()
///     .DefineRole("owner", "read", "edit", "delete", "share")
///     .DefineRole("editor", "read", "edit")
///     .DefineRole("viewer", "read")
///     .ResourceType("presentation", ResourceVisibility.Public)
///     .ResourceType("draft")
///     .Build();
/// </code>
/// </example>
public class GrantOptions
{
    private readonly List<(string Name, string[] Actions)> _roles = new();
    private readonly List<(string Name, ResourceVisibility Visibility)> _resourceTypes = new();

    /// <summary>The role <see cref="Services.IGrantService.GrantOwnerOnCreateAsync"/> grants and the last-owner rule protects. Must be declared and must allow <see cref="ShareAction"/>.</summary>
    public string OwnerRole { get; set; } = "owner";

    /// <summary>The action that lets a holder grant and revoke on a resource. Must be allowed by at least the owner role.</summary>
    public string ShareAction { get; set; } = "share";

    /// <summary>Declares a role and the actions it allows. Validated by <see cref="Build"/>.</summary>
    public GrantOptions DefineRole(string name, params string[] actions)
    {
        _roles.Add((name, actions ?? Array.Empty<string>()));
        return this;
    }

    /// <summary>
    /// Declares a resource type grants may be written for, and its visibility. <see cref="ResourceVisibility.Granted"/>
    /// is the default, so a type declared without thought hides its resources rather than revealing them.
    /// </summary>
    public GrantOptions ResourceType(string name, ResourceVisibility visibility = ResourceVisibility.Granted)
    {
        _resourceTypes.Add((name, visibility));
        return this;
    }

    /// <summary>Validates the declarations and returns the immutable table.</summary>
    /// <exception cref="GrantConfigurationException">Any declaration is invalid.</exception>
    public GrantRoleTable Build() => new(_roles, _resourceTypes, OwnerRole, ShareAction);
}
