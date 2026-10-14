using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Grants.Models;

namespace Birko.Security.Grants.Services;

/// <summary>
/// Checks and writes per-resource grants. Every method takes the tenant explicitly — the service never reads an
/// ambient tenant, so there is one source for it (rule 38). <c>null</c> means "no tenant in scope": only untenanted
/// grants apply. Nothing is cached, so a revoke takes effect on the next check.
/// </summary>
/// <remarks>
/// A role, action or resource type not declared in the <see cref="GrantRoleTable"/> throws
/// <see cref="GrantConfigurationException"/> from every method; a write an authorization rule forbids throws
/// <see cref="GrantRefusedException"/>.
/// </remarks>
public interface IGrantService
{
    /// <summary>
    /// Whether the user may perform <paramref name="action"/> on the resource: an admin may; otherwise a type-wide grant
    /// or a grant on exactly <paramref name="resourceId"/> must allow it. With <paramref name="resourceId"/> <c>null</c>
    /// (e.g. "create") only type-wide grants count.
    /// </summary>
    Task<bool> CanAsync(Guid userGuid, string resourceType, Guid? resourceId, string action, Guid? tenantGuid = null, CancellationToken ct = default);

    /// <summary>
    /// The resources of a type on which the user may perform <paramref name="action"/> — the read filter of a list
    /// endpoint passes "read" — see <see cref="GrantedResources"/>. Grants whose role does not allow the action are
    /// ignored, so a type-wide grant without "read" never makes every resource listable.
    /// </summary>
    Task<GrantedResources> ListResourcesAsync(Guid userGuid, string resourceType, string action, Guid? tenantGuid = null, CancellationToken ct = default);

    /// <summary>Every grant applying to the resource: exact ones and type-wide ones, flagged.</summary>
    Task<IReadOnlyList<GrantEntry>> ListGranteesAsync(string resourceType, Guid resourceId, Guid? tenantGuid = null, CancellationToken ct = default);

    /// <summary>
    /// Throws <see cref="GrantRefusedException"/> unless <paramref name="callerGuid"/> may grant <paramref name="role"/> on
    /// the resource — the rule <see cref="GrantAsync"/> applies to a new grant: an admin, or the share action plus every
    /// action of the role. Used to authorize an invitation before anything is written, and by a UI deciding whether to
    /// offer "share".
    /// </summary>
    Task AuthorizeGrantAsync(Guid callerGuid, string resourceType, Guid? resourceId, string role, Guid? tenantGuid = null, CancellationToken ct = default);

    /// <summary>
    /// Grants <paramref name="role"/> to <paramref name="userGuid"/>, replacing any role they already hold on exactly that
    /// (type, resource, tenant). The caller must be an admin, or hold the share action and every action of both the new
    /// role and the replaced one. Replacing the last owner's role is refused.
    /// </summary>
    Task<ResourceGrant> GrantAsync(Guid callerGuid, Guid userGuid, string resourceType, Guid? resourceId, string role, Guid? tenantGuid = null, CancellationToken ct = default);

    /// <summary>
    /// Ensures the user holds at least <paramref name="role"/>'s actions, never taking any away: a role that already
    /// allows them is kept as it is (nothing written, nothing authorized); a lesser role is upgraded and a missing one
    /// created, authorized as <see cref="GrantAsync"/>; a role whose actions neither contain nor are contained in
    /// <paramref name="role"/>'s is refused with <see cref="GrantErrorCodes.RoleConflict"/>. Decided under the same
    /// resource lock as the write, so a concurrent promotion is never overwritten. What redeeming an invitation uses.
    /// </summary>
    Task<ResourceGrant> GrantAtLeastAsync(Guid callerGuid, Guid userGuid, string resourceType, Guid? resourceId, string role, Guid? tenantGuid = null, CancellationToken ct = default);

    /// <summary>
    /// Removes the user's grant on exactly (type, resource, tenant) — every row with that key, should a backend without
    /// a unique index hold two. Returns <c>false</c> when there was none. Same authorization as <see cref="GrantAsync"/>
    /// against the removed role, except that anyone may remove their own grant; removing the last owner is refused.
    /// The reserved admin grant is not removed here (its type <c>"*"</c> is never declared).
    /// </summary>
    Task<bool> RevokeAsync(Guid callerGuid, Guid userGuid, string resourceType, Guid? resourceId, Guid? tenantGuid = null, CancellationToken ct = default);

    /// <summary>
    /// Makes the creator of a resource its owner, without an authorization check (the creator holds no grant yet).
    /// Idempotent for an existing owner. A resource that already has another owner is refused
    /// (<see cref="GrantErrorCodes.NotAuthorized"/>): ownership of an existing resource is granted, never claimed.
    /// </summary>
    Task<ResourceGrant> GrantOwnerOnCreateAsync(Guid userGuid, string resourceType, Guid resourceId, Guid? tenantGuid = null, CancellationToken ct = default);
}
