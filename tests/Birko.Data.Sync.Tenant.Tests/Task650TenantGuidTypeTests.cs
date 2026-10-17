using System;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tenant.Models;
using Birko.Data.Sync.Tenant.Providers;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tenant.Tests;

/// <summary>
/// TASK-650: a <c>TenantGuid</c> that is neither <see cref="Guid"/> nor <c>Guid?</c> could not be turned into a fetch
/// predicate, so a run scoped to one tenant fetched, compared and recorded every tenant's rows (writes were blocked).
/// A scoped run now refuses; an all-tenants run is unaffected.
/// </summary>
public class Task650TenantGuidTypeTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");

    private class StringTenantItem : AbstractModel
    {
        public string? TenantGuid { get; set; }
        public string? Name { get; set; }
    }

    private static TenantContext Context(Guid? tenant)
    {
        var context = new TenantContext();
        if (tenant.HasValue) context.SetTenant(tenant.Value);
        return context;
    }

    private static TenantSyncProvider<AsyncInMemoryStore<StringTenantItem>, StringTenantItem, Knowledge> Provider(
        AsyncInMemoryStore<StringTenantItem> remote, Guid? tenant)
        => new(new AsyncInMemoryStore<StringTenantItem>(), remote, new TestKnowledgeStore(), Context(tenant));

    [Fact]
    public async Task A_scoped_run_over_an_unscopable_TenantGuid_refuses()
    {
        var remote = new AsyncInMemoryStore<StringTenantItem>();
        await remote.CreateAsync(new StringTenantItem { Guid = Guid.NewGuid(), TenantGuid = "other", Name = "b" });
        var options = new TenantSyncOptions { Scope = "S", Direction = SyncDirection.Download };

        var preview = () => Provider(remote, TenantA).PreviewAsync(options);
        var sync = () => Provider(remote, TenantA).SyncAsync(options);

        (await preview.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("TenantGuid").And.Contain("String");
        await sync.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task An_all_tenants_run_over_the_same_type_still_works()
    {
        var remote = new AsyncInMemoryStore<StringTenantItem>();
        await remote.CreateAsync(new StringTenantItem { Guid = Guid.NewGuid(), TenantGuid = "other", Name = "b" });

        var context = Context(null);
        var provider = new TenantSyncProvider<AsyncInMemoryStore<StringTenantItem>, StringTenantItem, Knowledge>(
            new AsyncInMemoryStore<StringTenantItem>(), remote, new TestKnowledgeStore(), context);

        var preview = (await context.WithAllTenantsAsync(() => provider.PreviewAsync(new TenantSyncOptions { Scope = "S", Direction = SyncDirection.Download })))!;

        preview.ToCreate.Should().Be(1);
    }
}
