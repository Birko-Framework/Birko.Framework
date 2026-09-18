using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// SH-H054 — a nested <c>WithTenant</c> did not narrow reads inside an all-tenants scope.
///
/// <para><b>The defect.</b> All four <c>WithTenant</c> overloads saved and restored
/// <c>_currentTenantGuid</c> / <c>_currentTenantName</c> but never touched <c>_allTenantsScope</c>, while
/// <c>TenantFilter</c> computes <c>IsAllTenantsScope ? null : CurrentTenantGuid</c> — testing the flag
/// <i>first</i>. So the per-tenant admin loop the two scopes exist for,
/// <c>WithAllTenants(() =&gt; foreach (t) WithTenant(t, () =&gt; Read()))</c>, returned <b>every</b> tenant's
/// rows on <b>every</b> iteration.</para>
///
/// <para><b>The asymmetry was the tell.</b> Item writes test <c>HasTenant</c> first, so writes narrowed
/// while reads did not — two adjacent lines in the same class resolving the same question in opposite
/// orders. That is what made it a defect rather than a policy: nobody chose it.</para>
///
/// <para><b>The fix.</b> <c>WithTenant</c> suspends the all-tenants scope for its duration and restores it
/// on exit, so the innermost explicit scope wins. Restoring is what keeps the next loop iteration from
/// being left narrowed.</para>
///
/// <para><b>Deliberately NOT fixed here:</b> when <c>WithAllTenants</c> is the innermost scope, reads still
/// widen while item writes still narrow. That disagreement is TASK-127's open decision, not this defect;
/// the tests at the bottom pin it as a <i>baseline</i> so the decision has something to move from.</para>
/// </summary>
public class NestedTenantScopeTests
{
    public class Doc : AbstractModel, ITenant
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public string? Name { get; set; }
    }

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid TenantC = Guid.NewGuid();

    /// <summary>A store holding exactly one row per tenant.</summary>
    private static InMemoryStore<Doc> ThreeTenantStore()
    {
        var store = new InMemoryStore<Doc>();
        foreach (var (t, n) in new[] { (TenantA, "a"), (TenantB, "b"), (TenantC, "c") })
        {
            store.Create(new Doc { Guid = Guid.NewGuid(), TenantGuid = t, TenantName = n, Name = n });
        }
        return store;
    }

    private static AsyncInMemoryStore<Doc> ThreeTenantAsyncStore()
    {
        var store = new AsyncInMemoryStore<Doc>();
        foreach (var (t, n) in new[] { (TenantA, "a"), (TenantB, "b"), (TenantC, "c") })
        {
            store.CreateAsync(new Doc { Guid = Guid.NewGuid(), TenantGuid = t, TenantName = n, Name = n })
                 .GetAwaiter().GetResult();
        }
        return store;
    }

    // ── The reported defect ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_nested_WithTenant_narrows_reads_inside_an_all_tenants_scope()
    {
        var ctx = new TenantContext();
        var wrapper = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(ThreeTenantStore(), ctx);

        List<Doc>? seen = null;
        ctx.WithAllTenants(() => ctx.WithTenant(TenantA, "a", () => { seen = wrapper.Read().ToList(); }));

        seen.Should().NotBeNull();
        seen!.Should().HaveCount(1, "the innermost explicit scope wins");
        seen[0].TenantGuid.Should().Be(TenantA);
    }

    [Fact]
    public async Task A_nested_WithTenantAsync_narrows_reads_inside_an_all_tenants_scope()
    {
        var ctx = new TenantContext();
        var wrapper = new AsyncTenantBulkStoreWrapper<AsyncInMemoryStore<Doc>, Doc>(ThreeTenantAsyncStore(), ctx);

        List<Doc>? seen = null;
        await ctx.WithAllTenantsAsync(() => ctx.WithTenantAsync(TenantA, "a", async () =>
        {
            // Both ReadAsync overloads are reachable with no arguments; name the filter to disambiguate.
            seen = (await wrapper.ReadAsync(filter: null)).ToList();
        }));

        seen.Should().NotBeNull();
        seen!.Should().ContainSingle().Which.TenantGuid.Should().Be(TenantA);
    }

    [Fact]
    public void The_per_tenant_admin_loop_reads_each_tenants_rows_exactly_once()
    {
        // The motivating shape: without the fix this reads 3 rows per iteration (9 total) instead of 1.
        var ctx = new TenantContext();
        var wrapper = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(ThreeTenantStore(), ctx);
        var seenPerIteration = new List<int>();
        var seenTenants = new List<Guid>();

        ctx.WithAllTenants(() =>
        {
            foreach (var t in new[] { TenantA, TenantB, TenantC })
            {
                ctx.WithTenant(t, null, () =>
                {
                    var rows = wrapper.Read().ToList();
                    seenPerIteration.Add(rows.Count);
                    seenTenants.AddRange(rows.Select(r => r.TenantGuid));
                });
            }
        });

        seenPerIteration.Should().Equal(1, 1, 1);
        seenTenants.Should().Equal(TenantA, TenantB, TenantC);
    }

    [Fact]
    public void Exiting_a_nested_WithTenant_restores_the_all_tenants_scope()
    {
        var ctx = new TenantContext();
        var wrapper = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(ThreeTenantStore(), ctx);

        ctx.WithAllTenants(() =>
        {
            ctx.WithTenant(TenantA, "a", () => { });

            ctx.IsAllTenantsScope.Should().BeTrue("the flag is restored on exit, not left cleared");
            wrapper.Read().Should().HaveCount(3, "the next iteration must not be left narrowed");
        });
    }

    [Fact]
    public void The_scope_is_restored_even_when_the_body_throws()
    {
        var ctx = new TenantContext();

        ctx.WithAllTenants(() =>
        {
            var act = () => ctx.WithTenant(TenantA, "a", () => throw new InvalidOperationException("boom"));
            act.Should().Throw<InvalidOperationException>();

            ctx.IsAllTenantsScope.Should().BeTrue("restore happens in a finally");
        });
    }

    [Fact]
    public void All_four_WithTenant_overloads_suspend_the_all_tenants_scope()
    {
        // The four overloads duplicate the save/restore block, so each is asserted rather than assuming
        // the pattern was applied uniformly.
        var ctx = new TenantContext();
        var observed = new List<bool>();

        ctx.WithAllTenants(() =>
        {
            ctx.WithTenant(TenantA, null, () => observed.Add(ctx.IsAllTenantsScope));
            ctx.WithTenant(TenantA, null, () => { observed.Add(ctx.IsAllTenantsScope); return 0; });
            ctx.WithTenantAsync(TenantA, null, () => { observed.Add(ctx.IsAllTenantsScope); return Task.CompletedTask; })
               .GetAwaiter().GetResult();
            ctx.WithTenantAsync(TenantA, null, () => { observed.Add(ctx.IsAllTenantsScope); return Task.FromResult(0); })
               .GetAwaiter().GetResult();
        });

        observed.Should().Equal(false, false, false, false);
        ctx.IsAllTenantsScope.Should().BeFalse();
    }

    [Fact]
    public void Reads_and_item_writes_agree_when_WithTenant_is_the_innermost_scope()
    {
        // The rescoped criterion: the two lines that resolved precedence in opposite orders now agree for
        // the nested case. A read that returns a row must be a row the caller may also write.
        var ctx = new TenantContext();
        var store = ThreeTenantStore();
        var wrapper = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(store, ctx);

        ctx.WithAllTenants(() => ctx.WithTenant(TenantA, "a", () =>
        {
            var rows = wrapper.Read().ToList();
            rows.Should().ContainSingle();

            var mine = rows[0];
            wrapper.Invoking(w => w.Update(new Doc
            {
                Guid = mine.Guid,
                TenantGuid = TenantA,
                TenantName = "a",
                Name = "edited",
            })).Should().NotThrow("what the read returned, the write must accept");
        }));

        store.Items[store.Items.Keys.First(k => store.Items[k].TenantGuid == TenantA)]
             .Name.Should().Be("edited");
    }

    // ── Baseline for TASK-127 — current behaviour, NOT asserted as desired ────────────────────

    [Fact]
    public void BASELINE_with_WithAllTenants_innermost_reads_widen_while_item_writes_do_not()
    {
        // Pins today's behaviour so TASK-127 has a baseline to move from. This asymmetry is the open
        // DECISION, not the defect fixed here — do not read this test as endorsing it. If TASK-127
        // resolves the other way, this test is expected to change with it.
        var ctx = new TenantContext();
        var store = ThreeTenantStore();
        var wrapper = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(store, ctx);

        ctx.WithTenant(TenantA, "a", () => ctx.WithAllTenants(() =>
        {
            wrapper.Read().Should().HaveCount(3, "reads widen — TenantFilter tests IsAllTenantsScope first");

            var foreign = store.Items.Values.First(d => d.TenantGuid == TenantB);
            wrapper.Invoking(w => w.Update(new Doc
            {
                Guid = foreign.Guid,
                TenantGuid = TenantB,
                TenantName = "b",
                Name = "edited",
            })).Should().Throw<TenantMismatchException>(
                "item writes do NOT widen — BelongsToCurrentTenant tests HasTenant first");
        }));
    }

    [Fact]
    public void BASELINE_an_ambient_tenant_plus_WithAllTenants_still_widens_reads()
    {
        // The other half of TASK-127: the tenant arrives from middleware via SetTenant rather than
        // WithTenant, so this fix does not touch it and must not be assumed to.
        var ctx = new TenantContext();
        ctx.SetTenant(TenantA, "a");
        var wrapper = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(ThreeTenantStore(), ctx);

        ctx.WithAllTenants(() => wrapper.Read().Should().HaveCount(3));
        wrapper.Read().Should().ContainSingle("outside the scope the ambient tenant still applies");
    }
}
