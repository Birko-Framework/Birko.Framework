using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Tenant.Filters;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// CR-H108: coverage for ModelByTenant.Filter() (incl. the Guid.Empty/null short-circuit) and the
/// tenant store wrapper's stamping + BelongsToCurrentTenant authorization on Update/Delete.
/// </summary>
public class ModelByTenantAndWrapperTests
{
    public class Doc : AbstractModel, ITenant
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public string? Name { get; set; }
    }

    // ---- ModelByTenant.Filter ----

    [Fact]
    public void Filter_NullOrEmptyTenant_ReturnsBaseFilterOnly()
    {
        new ModelByTenant<Doc>(null).Filter().Should().BeNull();
        new ModelByTenant<Doc>(Guid.Empty).Filter().Should().BeNull();
    }

    [Fact]
    public void Filter_WithTenant_MatchesOnlyThatTenant()
    {
        var tenant = Guid.NewGuid();
        var predicate = new ModelByTenant<Doc>(tenant).Filter()!.Compile();

        predicate(new Doc { TenantGuid = tenant }).Should().BeTrue();
        predicate(new Doc { TenantGuid = Guid.NewGuid() }).Should().BeFalse();
    }

    [Fact]
    public void Filter_CombinesBaseFilterWithTenant()
    {
        var tenant = Guid.NewGuid();
        var predicate = new ModelByTenant<Doc>(tenant, d => d.Name == "keep").Filter()!.Compile();

        predicate(new Doc { TenantGuid = tenant, Name = "keep" }).Should().BeTrue();
        predicate(new Doc { TenantGuid = tenant, Name = "other" }).Should().BeFalse();
        predicate(new Doc { TenantGuid = Guid.NewGuid(), Name = "keep" }).Should().BeFalse();
    }

    // ---- Store wrapper ----

    private static AsyncTenantBulkStoreWrapper<AsyncInMemoryStore<Doc>, Doc> Wrap(
        AsyncInMemoryStore<Doc> inner, TenantContext ctx)
        => new(inner, ctx);

    [Fact]
    public async Task Create_StampsCurrentTenant()
    {
        var inner = new AsyncInMemoryStore<Doc>();
        var ctx = new TenantContext();
        var tenant = Guid.NewGuid();
        ctx.SetTenant(tenant, "Acme");
        var wrapper = Wrap(inner, ctx);

        var doc = new Doc { Guid = Guid.NewGuid(), Name = "x" };
        await wrapper.CreateAsync(new[] { doc });

        doc.TenantGuid.Should().Be(tenant);
    }

    [Fact]
    public async Task Read_OnlyReturnsCurrentTenantItems()
    {
        var inner = new AsyncInMemoryStore<Doc>();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await inner.CreateAsync(new[]
        {
            new Doc { Guid = Guid.NewGuid(), TenantGuid = tenantA, Name = "a" },
            new Doc { Guid = Guid.NewGuid(), TenantGuid = tenantB, Name = "b" },
        });

        var ctx = new TenantContext();
        ctx.SetTenant(tenantA);
        var wrapper = Wrap(inner, ctx);

        var results = (await wrapper.ReadAsync(filter: null)).ToList();

        results.Should().OnlyContain(d => d.TenantGuid == tenantA);
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task Update_ForeignTenantItem_Throws()
    {
        var inner = new AsyncInMemoryStore<Doc>();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid());
        var wrapper = Wrap(inner, ctx);

        var foreign = new Doc { Guid = Guid.NewGuid(), TenantGuid = Guid.NewGuid(), Name = "foreign" };

        await wrapper.Invoking(w => w.UpdateAsync(new[] { foreign }))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Delete_ForeignTenantItem_Throws()
    {
        var inner = new AsyncInMemoryStore<Doc>();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid());
        var wrapper = Wrap(inner, ctx);

        var foreign = new Doc { Guid = Guid.NewGuid(), TenantGuid = Guid.NewGuid() };

        await wrapper.Invoking(w => w.DeleteAsync(new[] { foreign }))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Update_OwnTenantItem_Succeeds()
    {
        var inner = new AsyncInMemoryStore<Doc>();
        var tenant = Guid.NewGuid();
        var ctx = new TenantContext();
        ctx.SetTenant(tenant);
        var wrapper = Wrap(inner, ctx);

        var doc = new Doc { Guid = Guid.NewGuid(), Name = "x" };
        await wrapper.CreateAsync(new[] { doc }); // stamped with tenant

        doc.Name = "updated";
        await wrapper.Invoking(w => w.UpdateAsync(new[] { doc })).Should().NotThrowAsync();
    }
}
