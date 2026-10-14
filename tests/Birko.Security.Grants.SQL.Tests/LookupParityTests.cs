using Birko.Data.InMemory.Stores;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Grants.SQL.Tests;

/// <summary>
/// Every <see cref="IResourceGrantStore"/> lookup must return the same rows from SQLite as from LINQ to objects. The
/// predicates are built by <c>GrantPredicates</c>, which branches on null to keep a null comparison an explicit
/// <c>IS NULL</c>, and combines expressions with a parameter rewrite. A translator that renders any of that differently
/// is caught here as a set difference.
/// </summary>
public class LookupParityTests : IDisposable
{
    private sealed class MemoryGrantStore : AsyncInMemoryStore<ResourceGrant>, IResourceGrantStore { }

    private readonly SqliteGrantsFixture _f = new();
    private readonly MemoryGrantStore _memory = new();

    private static readonly Guid U1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid U2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid T1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid T2 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid R1 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid R2 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static readonly Guid[] Users = { U1, U2 };
    private static readonly Guid?[] Tenants = { null, T1, T2 };
    private static readonly Guid?[] Resources = { null, R1, R2 };
    private static readonly string[] Types = { "doc", "draft" };

    public void Dispose() => _f.Dispose();

    // The lookups are default interface methods: reachable only through the interface type.
    private IResourceGrantStore Sql => _f.Grants;
    private IResourceGrantStore Mem => _memory;

    /// <summary>Exact, type-wide and admin grants, untenanted and in T1 / T2, for two users.</summary>
    private async Task SeedAsync()
    {
        var rows = new List<ResourceGrant>
        {
            G(U1, "doc", R1, "owner", null), G(U1, "doc", R2, "viewer", T1), G(U1, "doc", null, "editor", null),
            G(U1, "draft", R1, "viewer", T2), G(U1, "draft", null, "owner", T1),
            G(U2, "doc", R1, "editor", T1), G(U2, "doc", null, "viewer", T2), G(U2, "draft", R2, "owner", null),
            G(U1, "*", null, "*", null), G(U2, "*", null, "*", T1),
            G(U2, "doc", R1, "owner", null),
        };        foreach (var row in rows)
        {
            await Mem.CreateAsync(Copy(row));
            await Sql.CreateAsync(Copy(row));
        }
    }

    private static ResourceGrant G(Guid user, string type, Guid? id, string role, Guid? tenant) => new()
    {
        Guid = Guid.NewGuid(), UserGuid = user, ResourceType = type, ResourceId = id, Role = role, TenantGuid = tenant,
        CreatedAt = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc),
    };

    private static ResourceGrant Copy(ResourceGrant g) => new()
    {
        Guid = g.Guid, UserGuid = g.UserGuid, ResourceType = g.ResourceType, ResourceId = g.ResourceId, Role = g.Role,
        TenantGuid = g.TenantGuid, GrantedBy = g.GrantedBy, CreatedAt = g.CreatedAt,
    };

    private static HashSet<Guid> Ids(IEnumerable<ResourceGrant> grants) => grants.Select(g => g.Guid!.Value).ToHashSet();

    [Fact]
    public async Task SeededRows_AreTheSameInBothStores()
    {
        await SeedAsync();

        Ids(await Sql.ReadAsync(null, null, null, null)).Should().BeEquivalentTo(Ids(await Mem.ReadAsync(null, null, null, null)));
    }

    [Fact]
    public async Task ListForUser_Matches_ForEveryUserTypeAndTenant()
    {
        await SeedAsync();
        foreach (var user in Users)
        foreach (var type in Types)
        foreach (var tenant in Tenants)
        {
            Ids(await Sql.ListForUserAsync(user, type, tenant)).Should().BeEquivalentTo(
                Ids(await Mem.ListForUserAsync(user, type, tenant)), $"user {user}, type {type}, tenant {tenant}");
        }
    }

    [Fact]
    public async Task ListForResource_Matches_ForEveryTypeResourceAndTenant()
    {
        await SeedAsync();
        foreach (var type in Types)
        foreach (var id in new[] { R1, R2 })
        foreach (var tenant in Tenants)
        {
            Ids(await Sql.ListForResourceAsync(type, id, tenant)).Should().BeEquivalentTo(
                Ids(await Mem.ListForResourceAsync(type, id, tenant)), $"type {type}, resource {id}, tenant {tenant}");
        }
    }

    [Fact]
    public async Task GetExact_Matches_NullsIncluded()
    {
        await SeedAsync();
        foreach (var user in Users)
        foreach (var type in Types)
        foreach (var id in Resources)
        foreach (var tenant in Tenants)
        {
            var sql = await Sql.GetExactAsync(user, type, id, tenant);
            var memory = await Mem.GetExactAsync(user, type, id, tenant);
            sql?.Guid.Should().Be(memory?.Guid, $"user {user}, type {type}, resource {id}, tenant {tenant}");
            (sql == null).Should().Be(memory == null, $"user {user}, type {type}, resource {id}, tenant {tenant}");
        }
    }

    [Fact]
    public async Task CountRole_Matches()
    {
        await SeedAsync();
        foreach (var type in Types)
        foreach (var id in new[] { R1, R2 })
        foreach (var role in new[] { "owner", "viewer", "editor" })
        foreach (var tenant in Tenants)
        {
            (await Sql.CountRoleAsync(type, id, role, tenant)).Should().Be(
                await Mem.CountRoleAsync(type, id, role, tenant), $"type {type}, resource {id}, role {role}, tenant {tenant}");
        }
    }

    [Fact]
    public async Task AdminLookups_Match()
    {
        await SeedAsync();
        foreach (var user in Users)
        foreach (var tenant in Tenants)
        {
            (await Sql.HasAdminGrantAsync(user, tenant)).Should().Be(await Mem.HasAdminGrantAsync(user, tenant), $"user {user}, tenant {tenant}");
            Ids(await Sql.ListTypeWideForUserAsync(user, tenant)).Should().BeEquivalentTo(
                Ids(await Mem.ListTypeWideForUserAsync(user, tenant)), $"user {user}, tenant {tenant}");
        }
        foreach (var tenant in Tenants)
            (await Sql.CountAdminsAsync(tenant)).Should().Be(await Mem.CountAdminsAsync(tenant), $"tenant {tenant}");
    }

    [Fact]
    public async Task ATenantBoundGrant_IsNotReturned_WithoutItsTenant()
    {
        await SeedAsync();

        // U1's (doc, R2, viewer) lives in T1 only.
        (await Sql.ListForUserAsync(U1, "doc", null)).Should().NotContain(g => g.ResourceId == R2);
        (await Sql.ListForUserAsync(U1, "doc", T2)).Should().NotContain(g => g.ResourceId == R2);
        (await Sql.ListForUserAsync(U1, "doc", T1)).Should().Contain(g => g.ResourceId == R2);
        // An untenanted grant applies in every tenant.
        (await Sql.ListForUserAsync(U1, "doc", T2)).Should().Contain(g => g.ResourceId == R1);
    }
}
