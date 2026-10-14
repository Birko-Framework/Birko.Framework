using Birko.Security.AspNetCore;
using Birko.Security.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.Grants.Tests.AspNetCore;

/// <summary>An <see cref="ICurrentUser"/> whose every field the test sets.</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public TestCurrentUser(bool isAuthenticated, Guid? userGuid = null, Guid? tenantGuid = null)
    {
        IsAuthenticated = isAuthenticated;
        UserGuid = userGuid;
        TenantGuid = tenantGuid;
    }

    public Guid? UserGuid { get; }
    public string? Email => null;
    public Guid? TenantGuid { get; }
    public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
    public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
    public bool IsAuthenticated { get; }
    public string? GetClaim(string claimType) => null;
}

/// <summary>A host resolver returning fixed permissions per user, counting its calls.</summary>
public sealed class FixedPermissionResolver : IUserPermissionResolver
{
    private readonly Dictionary<Guid, HashSet<string>> _permissions = new();

    public FixedPermissionResolver Give(Guid user, params string[] permissions)
    {
        _permissions[user] = new HashSet<string>(permissions);
        return this;
    }

    public Task<IReadOnlySet<string>> GetAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<string>>(_permissions.TryGetValue(userId, out var p) ? p : new HashSet<string>());

    public Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "host-role" });
}

public static class ResultAssertions
{
    /// <summary>The status code an endpoint-filter result would write; 200 stands for "the endpoint ran".</summary>
    public static int StatusOf(object? result) => result switch
    {
        IStatusCodeHttpResult { StatusCode: int code } => code,
        "ran" => 200,
        _ => throw new InvalidOperationException($"Unexpected filter result: {result}"),
    };
}
