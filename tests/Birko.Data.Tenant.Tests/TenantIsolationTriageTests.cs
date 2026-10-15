using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Repositories;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>TASK-161, the medium findings in <c>tenant-isolation</c>, against the in-memory store.</summary>
public class TenantIsolationTriageTests
{
    public class Doc : AbstractModel, ITenant
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public int A { get; set; }
    }

    private static readonly Guid Mine = Guid.NewGuid();
    private static readonly Guid Theirs = Guid.NewGuid();

    private static (InMemoryStore<Doc> Inner, IBulkStore<Doc> Scoped) Seeded()
    {
        var inner = new InMemoryStore<Doc>();
        inner.Create(new Doc { TenantGuid = Mine, A = 1 });
        inner.Create(new Doc { TenantGuid = Mine, A = 2 });
        inner.Create(new Doc { TenantGuid = Theirs, A = 1 });
        var context = new TenantContext();
        context.SetTenant(Mine, "mine");
        return (inner, (IBulkStore<Doc>)((IStore<Doc>)inner).AsTenantAware(context));
    }

    /// <summary>
    /// SH-M337: a null filter forwarded through the non-nullable parameter became "this tenant's every row" once the
    /// tenant predicate was added, so the inner store's null guard never saw it. Measured on the old code: the
    /// tenant's two rows deleted, no exception.
    /// </summary>
    [Fact]
    public void A_null_filter_is_refused_before_the_tenant_scope_is_added()
    {
        var (inner, scoped) = Seeded();
        var act = () => scoped.Delete((Expression<Func<Doc, bool>>)null!);
        act.Should().Throw<ArgumentNullException>();
        var update = () => scoped.Update((Expression<Func<Doc, bool>>)null!, new PropertyUpdate<Doc>().Set(x => x.A, 9));
        update.Should().Throw<ArgumentNullException>();
        inner.Read(null, null, null, null).Count().Should().Be(3);
    }

    /// <summary>
    /// A filter that merely REDUCES to every row (here an empty-set NOT IN) is refused on the caller's filter, before
    /// the tenant predicate would make it look bounded. `x => true` is the explicit every-row spelling and stays allowed.
    /// </summary>
    [Fact]
    public void A_filter_that_reduces_to_every_row_is_refused_although_the_tenant_predicate_bounds_it()
    {
        var (inner, scoped) = Seeded();
        var none = Array.Empty<int>();
        var act = () => scoped.Delete(x => !none.Contains(x.A));
        act.Should().Throw<Exception>().Which.Should().NotBeOfType<NullReferenceException>();
        inner.Read(null, null, null, null).Count().Should().Be(3);
    }

    [Fact]
    public void A_named_filter_still_deletes_only_the_tenants_matching_rows()
    {
        var (inner, scoped) = Seeded();
        scoped.Delete(x => x.A == 1);
        inner.Read(null, null, null, null).Select(d => (d.TenantGuid == Mine, d.A)).Should().BeEquivalentTo(new[] { (true, 2), (false, 1) });
    }

    // ---- SH-M339: repository registration over a bulk store ----

    public class AsyncBulkDocRepository : AbstractAsyncBulkRepository<Doc>
    {
        public AsyncBulkDocRepository(IAsyncBulkStore<Doc> store) : base(store) { }
    }

    /// <summary>
    /// SH-M339: registration always built the NON-bulk wrapper, so a repository taking IAsyncBulkStore could not be
    /// constructed (MissingMethodException at resolve) and, where one could, lost the tenant-scoped filtered writes.
    /// </summary>
    [Fact]
    public async Task A_bulk_repository_over_a_bulk_store_resolves_and_its_filtered_writes_stay_in_the_tenant()
    {
        var context = new TenantContext();
        context.SetTenant(Mine, "mine");
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(context);
        services.AddTenantAsyncRepository<AsyncInMemoryStore<Doc>, AsyncBulkDocRepository, Doc>(ServiceLifetime.Singleton);
        using var root = services.BuildServiceProvider();

        var repository = root.GetRequiredService<AsyncBulkDocRepository>();
        var store = root.GetRequiredService<AsyncInMemoryStore<Doc>>();
        await store.CreateAsync(new Doc { TenantGuid = Mine, A = 1 });
        await store.CreateAsync(new Doc { TenantGuid = Theirs, A = 1 });

        await repository.DeleteAsync(x => x.A == 1);

        (await store.ReadAsync(null, null, null, null)).Select(d => d.TenantGuid).Should().Equal(Theirs);
    }
}
