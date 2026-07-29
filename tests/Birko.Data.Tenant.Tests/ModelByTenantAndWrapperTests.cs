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

    /// <summary>
    /// Only <c>null</c> means "no tenant in scope". Rewritten from
    /// <c>Filter_NullOrEmptyTenant_ReturnsBaseFilterOnly</c>, which also asserted
    /// <c>new ModelByTenant&lt;Doc&gt;(Guid.Empty).Filter().Should().BeNull()</c> — i.e. it recorded the
    /// <c>Guid.Empty</c> short-circuit as <i>intended</i>. See
    /// <see cref="Filter_EmptyTenant_IsFilteredOnLikeAnyOtherTenant"/> for why that was wrong. The
    /// <c>null</c> half stays correct: "no tenant" really is unfiltered, and
    /// <c>TenantStoreWrapper.EnsureTenantForStrict</c> is what refuses it under Strict.
    /// </summary>
    [Fact]
    public void Filter_NullTenant_ReturnsBaseFilterOnly()
    {
        new ModelByTenant<Doc>(null).Filter().Should().BeNull();
        new ModelByTenant<Doc>(null, d => d.Name == "keep").Filter()!.Compile()(
            new Doc { TenantGuid = Guid.NewGuid(), Name = "keep" })
            .Should().BeTrue("with no tenant in scope only the caller's own predicate applies");
    }

    /// <summary>
    /// Symbio TASK-295: <c>Guid.Empty</c> used to short-circuit to the base filter alongside <c>null</c>, so a
    /// caller scoped to <c>Guid.Empty</c> read <b>every</b> tenant's rows while <c>BelongsToCurrentTenant</c>
    /// still compared writes against <c>Guid.Empty</c> — reads failed open, writes failed closed. Measured in
    /// Symbio: a list read under an ambient <c>Guid.Empty</c> scope returned another tenant's rows, and the
    /// <c>PUT</c> that followed refused them as <c>Tenant.Mismatch</c>. "No tenant" was already expressible as
    /// <c>null</c>, so the special case bought nothing and cost isolation.
    /// </summary>
    [Fact]
    public void Filter_EmptyTenant_IsFilteredOnLikeAnyOtherTenant()
    {
        var predicate = new ModelByTenant<Doc>(Guid.Empty).Filter();

        predicate.Should().NotBeNull("a zero tenant is a tenant value, not 'unset' — it must still filter");
        var compiled = predicate!.Compile();
        compiled(new Doc { TenantGuid = Guid.Empty }).Should().BeTrue();
        compiled(new Doc { TenantGuid = Guid.NewGuid() }).Should().BeFalse(
            "this is the leak: another tenant's row must not be visible to a Guid.Empty scope");
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

    /// <summary>
    /// The store-level counterpart of <see cref="Filter_EmptyTenant_IsFilteredOnLikeAnyOtherTenant"/>, and the
    /// shape the defect was actually measured in (Symbio TASK-295): a wrapper whose ambient scope is
    /// <c>Guid.Empty</c> returned <b>every</b> tenant's rows. Asserted bidirectionally — the zero-tenant row
    /// IS returned (so this is a scoping change, not a blanket "return nothing") and the foreign rows are NOT.
    /// </summary>
    [Fact]
    public async Task Read_UnderEmptyTenantScope_DoesNotReturnOtherTenantsRows()
    {
        var inner = new AsyncInMemoryStore<Doc>();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await inner.CreateAsync(new[]
        {
            new Doc { Guid = Guid.NewGuid(), TenantGuid = Guid.Empty, Name = "zero-tenant" },
            new Doc { Guid = Guid.NewGuid(), TenantGuid = tenantA, Name = "a" },
            new Doc { Guid = Guid.NewGuid(), TenantGuid = tenantB, Name = "b" },
        });

        var ctx = new TenantContext();
        ctx.SetTenant(Guid.Empty); // HasTenant is true — Guid.Empty is a value, so Strict does not throw
        var wrapper = Wrap(inner, ctx);

        var results = (await wrapper.ReadAsync(filter: null)).ToList();

        results.Select(d => d.Name).Should().BeEquivalentTo(["zero-tenant"],
            "a Guid.Empty scope sees Guid.Empty rows and nothing else — it used to see all three");
        (await wrapper.CountAsync()).Should().Be(1, "CountAsync composes the same tenant filter");
    }

    [Fact]
    public async Task Read_InsideAllTenantsScope_SpansAllTenants_EvenWithTenantSet()
    {
        // Regression: WithAllTenants(...) must give truly cross-tenant reads even when a tenant is in
        // scope. Before the fix, TenantFilter unconditionally scoped to CurrentTenantGuid, so an admin
        // reading global/reference rows (TenantGuid == Guid.Empty) or other tenants' rows from within a
        // request scope silently saw only its own tenant.
        var inner = new AsyncInMemoryStore<Doc>();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await inner.CreateAsync(new[]
        {
            new Doc { Guid = Guid.NewGuid(), TenantGuid = Guid.Empty, Name = "global" },
            new Doc { Guid = Guid.NewGuid(), TenantGuid = tenantA, Name = "a" },
            new Doc { Guid = Guid.NewGuid(), TenantGuid = tenantB, Name = "b" },
        });

        var ctx = new TenantContext();
        ctx.SetTenant(tenantA); // a tenant IS in scope
        var wrapper = Wrap(inner, ctx);

        var results = await ctx.WithAllTenantsAsync(async () => (await wrapper.ReadAsync(filter: null)).ToList());

        results!.Select(d => d.Name).Should().BeEquivalentTo("global", "a", "b");
        // And the ambient tenant is restored once the scope exits.
        (await wrapper.ReadAsync(filter: null)).Should().OnlyContain(d => d.TenantGuid == tenantA);
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

    /// <summary>
    /// The refusal must be a <see cref="TenantMismatchException"/> — a host cannot otherwise tell "this row
    /// belongs to another tenant" apart from "you lack a permission", and reported them identically (a bare
    /// 403) until a Symbio consumer spent an hour hunting a permission for a tenancy problem. It still IS an
    /// <see cref="UnauthorizedAccessException"/>, so existing handlers keep working — asserted above.
    /// </summary>
    [Fact]
    public async Task Update_ForeignTenantItem_ThrowsTenantMismatchCarryingBothTenants()
    {
        var inner = new AsyncInMemoryStore<Doc>();
        var ctx = new TenantContext();
        var current = Guid.NewGuid();
        var owner = Guid.NewGuid();
        ctx.SetTenant(current);
        var wrapper = Wrap(inner, ctx);

        var foreign = new Doc { Guid = Guid.NewGuid(), TenantGuid = owner, Name = "foreign" };

        var thrown = await wrapper.Invoking(w => w.UpdateAsync(new[] { foreign }))
            .Should().ThrowAsync<TenantMismatchException>();

        thrown.Which.Should().BeAssignableTo<UnauthorizedAccessException>(
            "every existing catch (UnauthorizedAccessException) must keep working");
        thrown.Which.Operation.Should().Be("update");
        thrown.Which.EntityType.Should().Be(nameof(Doc));
        thrown.Which.ExpectedTenantGuid.Should().Be(current);
        thrown.Which.ActualTenantGuid.Should().Be(owner, "the log needs to name the owning tenant");
    }

    [Fact]
    public async Task Delete_ForeignTenantItem_ThrowsTenantMismatchNamingTheOperation()
    {
        var inner = new AsyncInMemoryStore<Doc>();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid());
        var wrapper = Wrap(inner, ctx);

        var thrown = await wrapper
            .Invoking(w => w.DeleteAsync(new[] { new Doc { Guid = Guid.NewGuid(), TenantGuid = Guid.NewGuid() } }))
            .Should().ThrowAsync<TenantMismatchException>();

        thrown.Which.Operation.Should().Be("delete");
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
