using Birko.Data.InMemory.Stores;
using Birko.Security.Grants.Internal;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.Stores;
using Birko.Time;

namespace Birko.Security.Grants.Tests;

public class InMemoryResourceGrantStore : AsyncInMemoryStore<ResourceGrant>, IResourceGrantStore
{
    /// <summary>
    /// Held after every count, so two concurrent writers both count before either writes. The in-memory store
    /// otherwise completes each call synchronously and a race test passes whether or not anything serialises it.
    /// </summary>
    public TimeSpan CountDelay { get; set; }

    protected override async Task<long> CountCoreAsync(System.Linq.Expressions.Expression<Func<ResourceGrant, bool>>? filter = null, CancellationToken ct = default)
    {
        var count = await base.CountCoreAsync(filter, ct);
        if (CountDelay > TimeSpan.Zero)
            await Task.Delay(CountDelay, ct);
        return count;
    }
}

/// <summary>Fresh store, table, clock and service per test — no shared mutable state, and a private lock map.</summary>
public sealed class GrantsFixture
{
    public const string Presentation = "presentation";
    public const string Draft = "draft";

    public InMemoryResourceGrantStore Store { get; } = new();
    public TestDateTimeProvider Clock { get; } = new();
    public GrantRoleTable Table { get; }
    public GrantService Service { get; }

    public GrantsFixture()
    {
        Table = DefaultOptions().Build();
        Service = new GrantService(Store, Table, Clock, adminSource: null, new KeyedLock());
    }

    public static GrantOptions DefaultOptions() => Configure(new GrantOptions());

    /// <summary>Declares the test roles and types on <paramref name="options"/> — what <c>AddBirkoGrants</c>' configure delegate receives.</summary>
    public static GrantOptions Configure(GrantOptions options) => options
        .DefineRole("owner", "read", "edit", "delete", "share")
        .DefineRole("editor", "read", "edit")
        .DefineRole("sharer", "read", "share")
        .DefineRole("viewer", "read")
        .ResourceType(Presentation, ResourceVisibility.Public)
        .ResourceType(Draft);

    /// <summary>Writes a grant directly, bypassing the service's rules (test setup).</summary>
    public async Task<ResourceGrant> SeedAsync(Guid user, string type, Guid? id, string role, Guid? tenant = null)
    {
        var grant = new ResourceGrant
        {
            UserGuid = user,
            ResourceType = type,
            ResourceId = id,
            Role = role,
            TenantGuid = tenant,
            CreatedAt = Clock.UtcNow,
        };
        await Store.CreateAsync(grant);
        return grant;
    }

    /// <summary>The reserved global admin grant — what TASK-589's bootstrap writes.</summary>
    public Task<ResourceGrant> SeedAdminAsync(Guid user, Guid? tenant = null)
        => SeedAsync(user, GrantRoleTable.Wildcard, null, GrantRoleTable.Wildcard, tenant);

    public async Task<List<ResourceGrant>> AllAsync() => (await Store.ReadAsync(null, null, null, null)).ToList();
}
