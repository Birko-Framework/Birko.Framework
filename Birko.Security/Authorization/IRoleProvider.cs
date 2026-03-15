using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.Authorization;

/// <summary>
/// Provides role information for users.
/// </summary>
public interface IRoleProvider
{
    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsInRoleAsync(Guid userId, string role, CancellationToken ct = default);
}

/// <summary>
/// Checks specific permissions for users.
/// </summary>
public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Authorization context for the current request.
/// </summary>
public class AuthorizationContext
{
    public Guid UserId { get; set; }
    public Guid? TenantGuid { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
    public IReadOnlyList<string> Permissions { get; set; } = [];

    public bool IsInRole(string role) => Roles.Contains(role);
    public bool HasPermission(string permission) => Permissions.Contains(permission);
}
