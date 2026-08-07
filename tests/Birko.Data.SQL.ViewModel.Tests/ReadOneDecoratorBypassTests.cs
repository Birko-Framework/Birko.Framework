using System;
using System.Linq.Expressions;
using Birko.Data.Filters;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.ViewModel.Tests;

/// <summary>
/// SH-H036 (TASK-125) — `ReadOne` must read through the decorator chain, not around it.
///
/// <para>The defect: the ordered `ReadOne` lived in an extension that read
/// `repository.Connector`, and `Connector` resolves as `Store?.GetUnwrappedStore&lt;…&gt;()?.Connector` —
/// `GetUnwrappedStore` walks `IStoreWrapper.GetInnerStore()` down to the **innermost** store. So no
/// decorator applied: `TenantStoreWrapper.Read` is what injects the `ModelByTenant` predicate, and without
/// it the read returned the first matching row from **any** tenant. Soft-delete, localization and audit
/// wrappers were dropped by the same call.</para>
///
/// <para>The trap that made it survive: `AbstractViewModelRepository.ReadOne(IFilter&lt;T&gt;?)` is
/// decorator-safe and the extension differed from it only by a second argument. C# prefers an applicable
/// instance method over an extension, so `ReadOne(filter)` was tenant-scoped while `ReadOne(filter, orderBy)`
/// silently was not — adding an ordering to a working call changed its isolation.</para>
/// </summary>
public class ReadOneDecoratorBypassTests
{
    public class Doc : AbstractModel, ITenant
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public string? Name { get; set; }
    }

    public class DocViewModel : ILoadable<Doc>
    {
        public Guid? Guid { get; set; }
        public Guid TenantGuid { get; set; }
        public string? Name { get; set; }

        public void LoadFrom(Doc model)
        {
            Guid = model.Guid;
            TenantGuid = model.TenantGuid;
            Name = model.Name;
        }

        public void StoreTo(Doc model)
        {
            model.Guid = Guid;
            model.TenantGuid = TenantGuid;
            model.Name = Name;
        }
    }

    /// Minimal repository over whatever store it is handed — the point is that the store may be decorated.
    private class DocRepository : AbstractViewModelRepository<DocViewModel, Doc>
    {
        public DocRepository(IStore<Doc> store) : base(store) { }

        protected override void MapToModel(DocViewModel source, Doc target) => source.StoreTo(target);
    }

    private sealed class NameIs : IFilter<Doc>
    {
        private readonly string _name;
        public NameIs(string name) => _name = name;
        public Expression<Func<Doc, bool>> Filter() => d => d.Name == _name;
    }

    private static (DocRepository repo, Guid mine, Guid theirs) TwoTenantFixture()
    {
        var inner = new InMemoryStore<Doc>();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        // "shared" exists in BOTH tenants, so a leaking read has something plausible to return and the
        // assertion cannot pass just because the other tenant's row failed the filter.
        inner.Create(new Doc { Guid = Guid.NewGuid(), TenantGuid = theirs, Name = "shared" });
        inner.Create(new Doc { Guid = Guid.NewGuid(), TenantGuid = mine, Name = "shared" });
        inner.Create(new Doc { Guid = Guid.NewGuid(), TenantGuid = theirs, Name = "theirs-only" });

        var ctx = new TenantContext();
        ctx.SetTenant(mine);
        var wrapped = new TenantStoreWrapper<InMemoryStore<Doc>, Doc>(inner, ctx);

        return (new DocRepository(wrapped), mine, theirs);
    }

    [Fact]
    public void ReadOne_UnderATenant_DoesNotReturnAnotherTenants_Row()
    {
        var (repo, mine, _) = TwoTenantFixture();

        // "theirs-only" exists, but not in my tenant. A decorator-bypassing read finds it.
        repo.ReadOne(new NameIs("theirs-only")).Should().BeNull(
            "the tenant decorator must narrow the read — the row exists, but not for this tenant");

        // And the row that IS mine still comes back, from my tenant, not the other one holding the same name.
        var own = repo.ReadOne(new NameIs("shared"));
        own.Should().NotBeNull("no over-filtering — the caller's own row must still be readable");
        own!.TenantGuid.Should().Be(mine);
    }

    [Fact]
    public void ReadOne_WithAnOrdering_IsAlsoTenantScoped()
    {
        // The overload that carried the defect. Before the fix this was the *only* form that reached the
        // bypass, because the 1-arg form binds to the safe instance method.
        var (repo, mine, _) = TwoTenantFixture();

        repo.ReadOne(new NameIs("theirs-only"), OrderBy<Doc>.By(d => d.Name!))
            .Should().BeNull("adding an ordering must not change the read's isolation");

        var own = repo.ReadOne(new NameIs("shared"), OrderBy<Doc>.By(d => d.Name!));
        own.Should().NotBeNull();
        own!.TenantGuid.Should().Be(mine);
    }

    [Fact]
    public void An_unfiltered_ReadOne_returns_this_tenants_row_not_whichever_is_first()
    {
        // The starkest form: no filter at all. The bypass returned physically-first row in the store, which
        // the fixture deliberately makes a foreign one.
        var (repo, mine, _) = TwoTenantFixture();

        repo.ReadOne()!.TenantGuid.Should().Be(mine);
        repo.ReadOne(null, OrderBy<Doc>.By(d => d.Name!))!.TenantGuid.Should().Be(mine);
    }

    [Fact]
    public void Ordering_degrades_rather_than_bypassing_when_the_decorator_is_not_a_bulk_store()
    {
        // CONTRACT PIN with a documented limitation, not a fix assertion. TenantStoreWrapper implements
        // IStore<T> but NOT IBulkReadStore<T>, so an ordering cannot be passed through it — the ordered
        // overload falls back to the unordered decorator-correct read. Correct-but-unordered is the right
        // trade against ordered-but-cross-tenant, and this pins that the fallback still returns a row
        // rather than throwing or returning nothing.
        var (repo, mine, _) = TwoTenantFixture();

        var result = repo.ReadOne(null, OrderBy<Doc>.ByDescending(d => d.Name!));

        result.Should().NotBeNull();
        result!.TenantGuid.Should().Be(mine);
    }

    [Fact]
    public void GetUnwrappedStore_strips_the_tenant_wrapper_which_is_why_reading_through_Connector_leaked()
    {
        // MECHANISM PIN, not evidence of this fix — this behaviour is unchanged and intentional.
        //
        // It is here because it is the reason the removed extension leaked, and because 69 other
        // `XStore` / `Connector` escape-hatch properties across the family resolve exactly this way. The
        // escape hatch is deliberate (backend-native features a portable store cannot express), but nothing
        // at those call sites says "this drops every decorator". This test says it, executably: unwrapping a
        // tenant-scoped store yields a store that returns another tenant's row.
        var inner = new InMemoryStore<Doc>();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        inner.Create(new Doc { Guid = Guid.NewGuid(), TenantGuid = theirs, Name = "theirs-only" });

        var ctx = new TenantContext();
        ctx.SetTenant(mine);
        var wrapped = new TenantStoreWrapper<InMemoryStore<Doc>, Doc>(inner, ctx);

        // Through the decorator: scoped, so the foreign row is invisible.
        wrapped.Read(d => d.Name == "theirs-only").Should().BeNull();

        // Unwrapped — what `repository.Connector` resolves to: the foreign row comes straight back.
        var unwrapped = wrapped.GetUnwrappedStore() as InMemoryStore<Doc>;
        unwrapped.Should().NotBeNull("GetUnwrappedStore walks IStoreWrapper down to the innermost store");
        unwrapped!.Read(d => d.Name == "theirs-only").Should().NotBeNull(
            "this is the leak the removed ReadOne extension had: no decorator, so no tenant predicate");
    }

    [Fact]
    public void Ordering_is_honoured_when_the_store_supports_bulk_reads()
    {
        // The other half of the above: with an undecorated bulk store the ordering IS applied, so the
        // fallback above is a property of the wrapper's surface and not of the new overload.
        var store = new InMemoryStore<Doc>();
        store.Create(new Doc { Guid = Guid.NewGuid(), Name = "b" });
        store.Create(new Doc { Guid = Guid.NewGuid(), Name = "a" });
        var repo = new DocRepository(store);

        repo.ReadOne(null, OrderBy<Doc>.By(d => d.Name!))!.Name.Should().Be("a");
        repo.ReadOne(null, OrderBy<Doc>.ByDescending(d => d.Name!))!.Name.Should().Be("b");
    }
}
