using System;
using Birko.Data.Models;

namespace Birko.Security.Grants.Models;

/// <summary>
/// One user's role on one resource, or on every resource of a type. Deliberately not an <c>ITenant</c>:
/// a null <see cref="TenantGuid"/> means "untenanted, applies in every tenant", which <c>ITenant</c>'s
/// non-null tenant cannot express, so tenant scoping is explicit in every query (<see cref="Stores.GrantPredicates"/>).
/// </summary>
public class ResourceGrant : AbstractModel
{
    public Guid UserGuid { get; set; }

    /// <summary>A resource type declared in the <see cref="GrantRoleTable"/>, or <see cref="GrantRoleTable.Wildcard"/> for the global admin grant.</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>The resource, or <c>null</c> for every resource of <see cref="ResourceType"/> (a type-wide grant).</summary>
    public Guid? ResourceId { get; set; }

    /// <summary>A role declared in the <see cref="GrantRoleTable"/>, or <see cref="GrantRoleTable.Wildcard"/> for the global admin grant.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The tenant the grant applies in, or <c>null</c> for every tenant.</summary>
    public Guid? TenantGuid { get; set; }

    /// <summary>Who wrote the grant; <c>null</c> for one the system wrote (owner-on-create, bootstrap).</summary>
    public Guid? GrantedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>A detached copy with a fresh identity, for re-creating a grant whose record was deleted.</summary>
    internal ResourceGrant CopyAsNew() => new()
    {
        UserGuid = UserGuid,
        ResourceType = ResourceType,
        ResourceId = ResourceId,
        Role = Role,
        TenantGuid = TenantGuid,
        GrantedBy = GrantedBy,
        CreatedAt = CreatedAt,
    };
}
