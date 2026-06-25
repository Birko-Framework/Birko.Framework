using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.AspNetCore.Authorization;

/// <summary>
/// Resolves the effective permission set for a user (and optional tenant scope) at request time.
/// Implementations are typically backed by the app's role/permission store + a cache,
/// and are invoked by <see cref="PermissionResolutionMiddleware"/> on every authenticated request.
///
/// Use case: when the permission set is too large to embed in a JWT (e.g. a TenantAdmin
/// with the aggregated permissions of every active module), keep the JWT to identity claims
/// only and resolve permissions server-side. Implementations should cache aggressively and
/// invalidate on role/permission mutations.
/// </summary>
public interface IUserPermissionResolver
{
    /// <summary>
    /// Returns the effective permission codes for <paramref name="userId"/> in the optional
    /// <paramref name="tenantId"/> scope. May return an empty set; never null.
    /// </summary>
    Task<IReadOnlySet<string>> GetAsync(Guid userId, Guid? tenantId, CancellationToken ct = default);

    /// <summary>
    /// Returns the effective role names for <paramref name="userId"/> in the optional
    /// <paramref name="tenantId"/> scope. Resolved server-side per request (parallel to
    /// <see cref="GetAsync"/>) so <see cref="ICurrentUser.Roles"/> works even when roles are
    /// kept out of the JWT. The default returns an empty set — implementations that back a
    /// role store should override it. Must never return null.
    /// </summary>
    Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
}
