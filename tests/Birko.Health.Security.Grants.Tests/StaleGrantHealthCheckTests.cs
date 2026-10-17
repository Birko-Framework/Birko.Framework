using System.Linq.Expressions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Stores;
using Birko.Health;
using Birko.Health.Security.Grants;
using Birko.Security.Grants;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Security.Grants.Tests;

/// <summary>TASK-654: the stale-grant check is Healthy with none, Degraded naming them, and Degraded when it cannot read.</summary>
public class StaleGrantHealthCheckTests
{
    private class InMemoryGrantStore : AsyncInMemoryStore<ResourceGrant>, IResourceGrantStore { }

    private sealed class FailingGrantStore : InMemoryGrantStore
    {
        protected override Task<IEnumerable<ResourceGrant>> ReadCoreAsync(Expression<Func<ResourceGrant, bool>>? filter = null,
            OrderBy<ResourceGrant>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
            => throw new InvalidOperationException("the grant store is unreachable");
    }

    private static readonly GrantRoleTable Table = new GrantOptions()
        .DefineRole("owner", "read", "edit", "share")
        .DefineRole("editor", "read", "edit")
        .ResourceType("presentation")
        .Build();

    private static async Task<InMemoryGrantStore> Seeded(params (string Type, string Role)[] grants)
    {
        var store = new InMemoryGrantStore();
        foreach (var (type, role) in grants)
        {
            await store.CreateAsync(new ResourceGrant { UserGuid = Guid.NewGuid(), ResourceType = type, Role = role, CreatedAt = DateTime.UtcNow });
        }
        return store;
    }

    [Fact]
    public async Task Healthy_when_every_grant_is_declared_and_the_admin_grant_is_ignored()
    {
        var store = await Seeded(("presentation", "editor"), (GrantRoleTable.Wildcard, GrantRoleTable.Wildcard));

        var result = await new StaleGrantHealthCheck(store, Table).CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data!["stale"].Should().Be(0);
    }

    [Fact]
    public async Task Degraded_with_the_stale_grants_named()
    {
        var store = await Seeded(("presentation", "editor"), ("presentation", "reviewer"), ("draft", "editor"));

        var result = await new StaleGrantHealthCheck(store, Table).CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded, "a stale grant needs a person, not a load balancer");
        result.Description.Should().Contain("2 stored grant(s)").And.Contain("'reviewer'");
        result.Data!["stale"].Should().Be(2);
        result.Data["roleNotDeclared"].Should().Be(1);
        result.Data["resourceTypeNotDeclared"].Should().Be(1);
        ((IEnumerable<string>)result.Data["staleGrants"]).Should().HaveCount(2)
            .And.Contain(s => s.Contains("undeclared role 'reviewer'"))
            .And.Contain(s => s.Contains("undeclared resource type 'draft'"));
    }

    [Fact]
    public async Task Degraded_never_Healthy_when_the_store_cannot_be_read()
    {
        var result = await new StaleGrantHealthCheck(new FailingGrantStore(), Table).CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("could not be checked").And.Contain("unreachable");
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task The_listing_is_capped_and_the_count_is_not()
    {
        var store = await Seeded(Enumerable.Range(0, StaleGrantHealthCheck.MaxListed + 5).Select(_ => ("presentation", "gone")).ToArray());

        var result = await new StaleGrantHealthCheck(store, Table).CheckAsync();

        result.Data!["stale"].Should().Be(StaleGrantHealthCheck.MaxListed + 5);
        ((IEnumerable<string>)result.Data["staleGrants"]).Should().HaveCount(StaleGrantHealthCheck.MaxListed);
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        FluentActions.Invoking(() => new StaleGrantHealthCheck(null!, Table)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new StaleGrantHealthCheck(new InMemoryGrantStore(), null!)).Should().Throw<ArgumentNullException>();
    }
}
