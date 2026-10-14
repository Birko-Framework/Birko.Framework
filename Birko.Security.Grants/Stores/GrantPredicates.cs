using System;
using System.Linq.Expressions;
using Birko.Security.Grants.Models;

namespace Birko.Security.Grants.Stores;

/// <summary>
/// The one producer of every grant query. Each predicate has a separate branch for a null argument rather than
/// comparing against a nullable variable: <c>g.TenantGuid == tenant</c> with <c>tenant == null</c> is "is null" in
/// LINQ to objects but <c>= NULL</c> — match nothing — in SQL (rule 5). So a null is always rendered as an explicit
/// <c>== null</c> term, and a value as an equality on a non-null captured <see cref="Guid"/>.
/// </summary>
/// <remarks>
/// Two tenant meanings, never mixed:
/// <list type="bullet">
/// <item><b>In scope</b> (reads, checks): with a tenant, untenanted grants plus that tenant's; with none, untenanted only.
/// A tenant-bound grant never matches outside its tenant (rules 38/39).</item>
/// <item><b>Exact</b> (one grant's identity, last-owner counts): the tenant column equals the argument, null included.</item>
/// </list>
/// </remarks>
internal static class GrantPredicates
{
    /// <summary>The user's grants on <paramref name="resourceType"/> — exact and type-wide — in tenant scope.</summary>
    public static Expression<Func<ResourceGrant, bool>> ForUser(Guid userGuid, string resourceType, Guid? tenantGuid)
    {
        if (tenantGuid is not Guid tenant)
            return g => g.UserGuid == userGuid && g.ResourceType == resourceType && g.TenantGuid == null;
        return g => g.UserGuid == userGuid && g.ResourceType == resourceType && (g.TenantGuid == null || g.TenantGuid == tenant);
    }

    /// <summary>Every grant that applies to one resource — on it, or type-wide — in tenant scope.</summary>
    public static Expression<Func<ResourceGrant, bool>> ForResource(string resourceType, Guid resourceId, Guid? tenantGuid)
    {
        if (tenantGuid is not Guid tenant)
            return g => g.ResourceType == resourceType && (g.ResourceId == null || g.ResourceId == resourceId) && g.TenantGuid == null;
        return g => g.ResourceType == resourceType && (g.ResourceId == null || g.ResourceId == resourceId)
            && (g.TenantGuid == null || g.TenantGuid == tenant);
    }

    /// <summary>The user's global admin grant (<see cref="GrantRoleTable.Wildcard"/> type and role), in tenant scope.</summary>
    public static Expression<Func<ResourceGrant, bool>> Admin(Guid userGuid, Guid? tenantGuid)
    {
        const string wildcard = GrantRoleTable.Wildcard;
        if (tenantGuid is not Guid tenant)
            return g => g.UserGuid == userGuid && g.ResourceType == wildcard && g.Role == wildcard && g.ResourceId == null && g.TenantGuid == null;
        return g => g.UserGuid == userGuid && g.ResourceType == wildcard && g.Role == wildcard && g.ResourceId == null
            && (g.TenantGuid == null || g.TenantGuid == tenant);
    }

    /// <summary>The one grant identified by (user, type, resource, tenant) — every key exact, nulls included.</summary>
    public static Expression<Func<ResourceGrant, bool>> Exact(Guid userGuid, string resourceType, Guid? resourceId, Guid? tenantGuid)
    {
        var key = ByKey(resourceType, resourceId, tenantGuid);
        return And(g => g.UserGuid == userGuid, key);
    }

    /// <summary>Grants of <paramref name="role"/> on exactly (type, resource, tenant) — what the last-owner rule counts.</summary>
    public static Expression<Func<ResourceGrant, bool>> RoleOn(string resourceType, Guid resourceId, string role, Guid? tenantGuid)
    {
        var key = ByKey(resourceType, resourceId, tenantGuid);
        return And(g => g.Role == role, key);
    }

    private static Expression<Func<ResourceGrant, bool>> ByKey(string resourceType, Guid? resourceId, Guid? tenantGuid)
    {
        return (resourceId, tenantGuid) switch
        {
            (null, null) => g => g.ResourceType == resourceType && g.ResourceId == null && g.TenantGuid == null,
            (null, Guid t) => g => g.ResourceType == resourceType && g.ResourceId == null && g.TenantGuid == t,
            (Guid r, null) => g => g.ResourceType == resourceType && g.ResourceId == r && g.TenantGuid == null,
            (Guid r, Guid t) => g => g.ResourceType == resourceType && g.ResourceId == r && g.TenantGuid == t,
        };
    }

    private static Expression<Func<ResourceGrant, bool>> And(
        Expression<Func<ResourceGrant, bool>> left, Expression<Func<ResourceGrant, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rightBody = new ReplaceParameter(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<ResourceGrant, bool>>(Expression.AndAlso(left.Body, rightBody), parameter);
    }

    private sealed class ReplaceParameter : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ReplaceParameter(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : node;
    }
}
