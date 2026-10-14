using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Security.Grants;

/// <summary>
/// The validated, immutable role → action table and the declared resource types. Built by <see cref="GrantOptions.Build"/>.
/// Every lookup of something undeclared throws <see cref="GrantConfigurationException"/> instead of answering "no",
/// so a typo in a role or action name fails loudly rather than silently denying (or granting).
/// </summary>
public sealed class GrantRoleTable
{
    /// <summary>
    /// Reserved name. The global admin grant is <c>{ResourceType = "*", ResourceId = null, Role = "*"}</c>;
    /// no consumer role, action or resource type may use it, so nothing a consumer declares can collide with it.
    /// </summary>
    public const string Wildcard = "*";

    private readonly Dictionary<string, IReadOnlySet<string>> _roles;
    private readonly Dictionary<string, ResourceVisibility> _resourceTypes;
    private readonly HashSet<string> _actions;

    public string OwnerRole { get; }
    public string ShareAction { get; }

    internal GrantRoleTable(
        IEnumerable<(string Name, string[] Actions)> roles,
        IEnumerable<(string Name, ResourceVisibility Visibility)> resourceTypes,
        string ownerRole,
        string shareAction)
    {
        _roles = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        _actions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, actions) in roles)
        {
            RequireName(name, "Role");
            if (_roles.ContainsKey(name))
                throw new GrantConfigurationException($"Role '{name}' is declared twice.");
            if (actions.Length == 0)
                throw new GrantConfigurationException($"Role '{name}' allows no actions.");

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in actions)
            {
                RequireName(action, $"An action of role '{name}'");
                set.Add(action);
                _actions.Add(action);
            }
            _roles[name] = set;
        }

        _resourceTypes = new Dictionary<string, ResourceVisibility>(StringComparer.Ordinal);
        foreach (var (name, visibility) in resourceTypes)
        {
            RequireName(name, "Resource type");
            if (!Enum.IsDefined(visibility))
                throw new GrantConfigurationException($"Resource type '{name}' has an undefined visibility ({(int)visibility}).");
            if (!_resourceTypes.TryAdd(name, visibility))
                throw new GrantConfigurationException($"Resource type '{name}' is declared twice.");
        }
        if (_resourceTypes.Count == 0)
            throw new GrantConfigurationException("No resource type is declared; grants could not be written for anything.");

        RequireName(shareAction, nameof(GrantOptions.ShareAction));
        if (!_actions.Contains(shareAction))
            throw new GrantConfigurationException($"Share action '{shareAction}' is not allowed by any declared role.");

        RequireName(ownerRole, nameof(GrantOptions.OwnerRole));
        if (!_roles.TryGetValue(ownerRole, out var ownerActions))
            throw new GrantConfigurationException($"Owner role '{ownerRole}' is not declared.");
        if (!ownerActions.Contains(shareAction))
            throw new GrantConfigurationException($"Owner role '{ownerRole}' must allow the share action '{shareAction}'.");

        OwnerRole = ownerRole;
        ShareAction = shareAction;
    }

    /// <summary>The declared roles.</summary>
    public IEnumerable<string> Roles => _roles.Keys;

    /// <summary>The declared resource types.</summary>
    public IEnumerable<string> ResourceTypes => _resourceTypes.Keys;

    /// <summary>The actions <paramref name="role"/> allows.</summary>
    /// <exception cref="GrantConfigurationException">The role is not declared.</exception>
    public IReadOnlySet<string> ActionsOf(string role)
    {
        if (role != null && _roles.TryGetValue(role, out var actions))
            return actions;
        throw new GrantConfigurationException($"Role '{role}' is not declared in the grant role table.");
    }

    /// <summary>Whether <paramref name="role"/> allows <paramref name="action"/>.</summary>
    /// <exception cref="GrantConfigurationException">The role or the action is not declared.</exception>
    public bool Allows(string role, string action)
    {
        RequireAction(action);
        return ActionsOf(role).Contains(action);
    }

    /// <summary>The declared visibility of <paramref name="resourceType"/>.</summary>
    /// <exception cref="GrantConfigurationException">The resource type is not declared.</exception>
    public ResourceVisibility VisibilityOf(string resourceType)
    {
        if (resourceType != null && _resourceTypes.TryGetValue(resourceType, out var visibility))
            return visibility;
        throw new GrantConfigurationException($"Resource type '{resourceType}' is not declared in the grant role table.");
    }

    /// <exception cref="GrantConfigurationException">The action is not allowed by any declared role.</exception>
    public void RequireAction(string action)
    {
        if (action == null || !_actions.Contains(action))
            throw new GrantConfigurationException($"Action '{action}' is not declared by any role in the grant role table.");
    }

    /// <exception cref="GrantConfigurationException">The role is not declared.</exception>
    public void RequireRole(string role) => ActionsOf(role);

    /// <exception cref="GrantConfigurationException">The resource type is not declared.</exception>
    public void RequireResourceType(string resourceType) => VisibilityOf(resourceType);

    private static void RequireName(string name, string what)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new GrantConfigurationException($"{what} name is empty.");
        if (name == Wildcard)
            throw new GrantConfigurationException($"{what} name '{Wildcard}' is reserved for the global admin grant.");
        // Type-wide grants surface as "{type}:{action}" permission strings; a ':' inside a name would let type "a" with
        // action "b:c" produce the same string as type "a:b" with action "c", and so grant the other type's rights.
        if (name.Contains(':'))
            throw new GrantConfigurationException($"{what} name '{name}' contains ':', the separator of the permission strings grants surface as.");
    }
}
