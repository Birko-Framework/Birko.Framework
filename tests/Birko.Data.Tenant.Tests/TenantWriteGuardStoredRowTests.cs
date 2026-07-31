using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// SH-H047 — the item-level tenant write guard authorized against the caller-supplied item.
///
/// <para><b>The defect.</b> <c>BelongsToCurrentTenant</c> compared <c>item.TenantGuid</c> to the ambient
/// tenant. <c>ITenant.TenantGuid</c> is a public settable property, routinely model-bound straight from a
/// request body, so it is the caller's <i>assertion</i> about a row rather than a fact about it. The inner
/// stores then key the write on the primary field alone — <c>DataBaseStore.UpdateCore</c>/<c>DeleteCore</c>
/// build their conditions from <c>GetPrimaryFields</c>, <c>AbstractInMemoryStore</c> keys on
/// <c>data.Guid</c> — and no tenant term is added anywhere. So a caller in tenant <i>t</i> submitting
/// <c>{ Guid = &lt;a row belonging to tenant u&gt;, TenantGuid = t }</c> passed the guard and overwrote or
/// deleted tenant <i>u</i>'s row. The guard checked the attacker's claim about the row instead of the row.</para>
///
/// <para><b>The fix.</b> The write paths read the targeted row back — deliberately <i>unscoped</i> by
/// tenant, because a tenant-scoped read returns <c>null</c> for a foreign row and so cannot tell it apart
/// from a row that does not exist — and authorize against that stored row. A second defect in the same
/// method is closed alongside it: <c>Update</c> forwarded the caller's <c>TenantGuid</c> verbatim, so an
/// owner could <i>re-home</i> their own row into another tenant; the persisted tenant is now restored onto
/// the item before it is handed to the inner store.</para>
///
/// <para>The pre-existing payload check survives only for the case where <b>no row exists</b> — it was
/// never authorization (a caller sets <c>TenantGuid</c> to whatever passes), but it still stops an
/// upserting inner store from being made to create a row homed in another tenant.</para>
///
/// <para>The InMemory store is the faithful stand-in here: it keys writes on <c>data.Guid</c> alone,
/// exactly the mechanism the finding describes.</para>
/// </summary>
public class TenantWriteGuardStoredRowTests
{
    public class Doc : AbstractModel, ITenant
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public string? Name { get; set; }
    }

    private static readonly Guid Attacker = Guid.NewGuid();
    private static readonly Guid Victim = Guid.NewGuid();

    private static TenantContext CtxFor(Guid tenant, string name)
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenant, name);
        return ctx;
    }

    /// <summary>A store already holding one row owned by the victim tenant, plus that row's Guid.</summary>
    private static (InMemoryStore<Doc> store, Guid rowGuid) StoreWithVictimRow()
    {
        var store = new InMemoryStore<Doc>();
        var guid = Guid.NewGuid();
        store.Create(new Doc { Guid = guid, TenantGuid = Victim, TenantName = "Victim", Name = "victim-data" });
        return (store, guid);
    }

    private static async Task<(AsyncInMemoryStore<Doc> store, Guid rowGuid)> AsyncStoreWithVictimRowAsync()
    {
        var store = new AsyncInMemoryStore<Doc>();
        var guid = Guid.NewGuid();
        await store.CreateAsync(new Doc { Guid = guid, TenantGuid = Victim, TenantName = "Victim", Name = "victim-data" });
        return (store, guid);
    }

    /// <summary>The attack payload: another tenant's row Guid, stamped with the attacker's own tenant.</summary>
    private static Doc AttackPayload(Guid victimRowGuid) => new()
    {
        Guid = victimRowGuid,
        TenantGuid = Attacker,
        TenantName = "Attacker",
        Name = "overwritten",
    };

    // ── The attack: a payload claiming our tenant over another tenant's row Guid ──────────────

    [Fact]
    public void Update_refuses_another_tenants_row_even_when_the_payload_claims_our_tenant()
    {
        var (store, rowGuid) = StoreWithVictimRow();
        var wrapper = new TenantStoreWrapper<InMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));

        wrapper.Invoking(w => w.Update(AttackPayload(rowGuid)))
            .Should().Throw<TenantMismatchException>()
            .Which.ActualTenantGuid.Should().Be(Victim, "the refusal reports the tenant that actually owns the row");

        store.Items[rowGuid].Name.Should().Be("victim-data", "the target row must be untouched");
        store.Items[rowGuid].TenantGuid.Should().Be(Victim);
    }

    [Fact]
    public void Delete_refuses_another_tenants_row_even_when_the_payload_claims_our_tenant()
    {
        var (store, rowGuid) = StoreWithVictimRow();
        var wrapper = new TenantStoreWrapper<InMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));

        wrapper.Invoking(w => w.Delete(AttackPayload(rowGuid)))
            .Should().Throw<TenantMismatchException>();

        store.Items.Should().ContainKey(rowGuid, "the target row must survive");
    }

    [Fact]
    public async Task UpdateAsync_refuses_another_tenants_row_even_when_the_payload_claims_our_tenant()
    {
        var (store, rowGuid) = await AsyncStoreWithVictimRowAsync();
        var wrapper = new AsyncTenantStoreWrapper<AsyncInMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));

        await wrapper.Awaiting(w => w.UpdateAsync(AttackPayload(rowGuid)))
            .Should().ThrowAsync<TenantMismatchException>();

        store.Items[rowGuid].Name.Should().Be("victim-data");
    }

    [Fact]
    public async Task DeleteAsync_refuses_another_tenants_row_even_when_the_payload_claims_our_tenant()
    {
        var (store, rowGuid) = await AsyncStoreWithVictimRowAsync();
        var wrapper = new AsyncTenantStoreWrapper<AsyncInMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));

        await wrapper.Awaiting(w => w.DeleteAsync(AttackPayload(rowGuid)))
            .Should().ThrowAsync<TenantMismatchException>();

        store.Items.Should().ContainKey(rowGuid);
    }

    [Fact]
    public void Bulk_Update_and_Delete_refuse_another_tenants_row()
    {
        var (updateStore, updateGuid) = StoreWithVictimRow();
        var bulkUpdate = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(updateStore, CtxFor(Attacker, "Attacker"));
        bulkUpdate.Invoking(w => w.Update(new[] { AttackPayload(updateGuid) }))
            .Should().Throw<TenantMismatchException>();
        updateStore.Items[updateGuid].Name.Should().Be("victim-data");

        var (deleteStore, deleteGuid) = StoreWithVictimRow();
        var bulkDelete = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(deleteStore, CtxFor(Attacker, "Attacker"));
        bulkDelete.Invoking(w => w.Delete(new[] { AttackPayload(deleteGuid) }))
            .Should().Throw<TenantMismatchException>();
        deleteStore.Items.Should().ContainKey(deleteGuid);
    }

    [Fact]
    public async Task AsyncBulk_Update_and_Delete_refuse_another_tenants_row()
    {
        var (updateStore, updateGuid) = await AsyncStoreWithVictimRowAsync();
        var bulkUpdate = new AsyncTenantBulkStoreWrapper<AsyncInMemoryStore<Doc>, Doc>(updateStore, CtxFor(Attacker, "Attacker"));
        await bulkUpdate.Awaiting(w => w.UpdateAsync(new[] { AttackPayload(updateGuid) }))
            .Should().ThrowAsync<TenantMismatchException>();
        updateStore.Items[updateGuid].Name.Should().Be("victim-data");

        var (deleteStore, deleteGuid) = await AsyncStoreWithVictimRowAsync();
        var bulkDelete = new AsyncTenantBulkStoreWrapper<AsyncInMemoryStore<Doc>, Doc>(deleteStore, CtxFor(Attacker, "Attacker"));
        await bulkDelete.Awaiting(w => w.DeleteAsync(new[] { AttackPayload(deleteGuid) }))
            .Should().ThrowAsync<TenantMismatchException>();
        deleteStore.Items.Should().ContainKey(deleteGuid);
    }

    [Fact]
    public void Save_routes_an_existing_Guid_through_the_stored_row_guard()
    {
        // Save() dispatches to Update for a non-empty Guid, so the attack is reachable through it too.
        var (store, rowGuid) = StoreWithVictimRow();
        var wrapper = new TenantStoreWrapper<InMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));

        wrapper.Invoking(w => w.Save(AttackPayload(rowGuid)))
            .Should().Throw<TenantMismatchException>();

        store.Items[rowGuid].Name.Should().Be("victim-data");
    }

    // ── The payload is not consulted when a row exists ───────────────────────────────────────

    [Fact]
    public void A_payload_TenantGuid_disagreeing_with_both_the_ambient_tenant_and_the_stored_row_is_ignored()
    {
        // The sharp end of the acceptance criterion, and the direction the old guard got wrong the other
        // way round: the row IS ours, the payload claims a third tenant. Authorization must follow the
        // stored row, so the write is allowed — where the old caller-trusting guard refused it.
        var store = new InMemoryStore<Doc>();
        var rowGuid = Guid.NewGuid();
        store.Create(new Doc { Guid = rowGuid, TenantGuid = Attacker, TenantName = "Attacker", Name = "ours" });

        var wrapper = new TenantStoreWrapper<InMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));
        var thirdTenant = Guid.NewGuid();

        wrapper.Invoking(w => w.Update(new Doc
        {
            Guid = rowGuid,
            TenantGuid = thirdTenant,
            TenantName = "Somewhere else",
            Name = "edited",
        })).Should().NotThrow("the stored row is ours; the payload's claim is not an input to the decision");

        store.Items[rowGuid].Name.Should().Be("edited");
    }

    // ── Re-homing ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_cannot_re_home_a_row_we_own_by_submitting_a_different_TenantGuid()
    {
        var store = new InMemoryStore<Doc>();
        var rowGuid = Guid.NewGuid();
        store.Create(new Doc { Guid = rowGuid, TenantGuid = Attacker, TenantName = "Attacker", Name = "ours" });

        var wrapper = new TenantStoreWrapper<InMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));
        wrapper.Update(new Doc { Guid = rowGuid, TenantGuid = Victim, TenantName = "Victim", Name = "edited" });

        store.Items[rowGuid].TenantGuid.Should().Be(Attacker, "the persisted tenant is not the caller's to change");
        store.Items[rowGuid].TenantName.Should().Be("Attacker");
        store.Items[rowGuid].Name.Should().Be("edited", "the rest of the payload still applies");
    }

    [Fact]
    public async Task UpdateAsync_cannot_re_home_a_row_we_own()
    {
        var store = new AsyncInMemoryStore<Doc>();
        var rowGuid = Guid.NewGuid();
        await store.CreateAsync(new Doc { Guid = rowGuid, TenantGuid = Attacker, TenantName = "Attacker", Name = "ours" });

        var wrapper = new AsyncTenantStoreWrapper<AsyncInMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));
        await wrapper.UpdateAsync(new Doc { Guid = rowGuid, TenantGuid = Victim, Name = "edited" });

        store.Items[rowGuid].TenantGuid.Should().Be(Attacker);
    }

    [Fact]
    public void Bulk_Update_cannot_re_home_a_row_we_own()
    {
        var store = new InMemoryStore<Doc>();
        var rowGuid = Guid.NewGuid();
        store.Create(new Doc { Guid = rowGuid, TenantGuid = Attacker, TenantName = "Attacker", Name = "ours" });

        var wrapper = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));
        wrapper.Update(new[] { new Doc { Guid = rowGuid, TenantGuid = Victim, Name = "edited" } });

        store.Items[rowGuid].TenantGuid.Should().Be(Attacker);
    }

    // ── Legitimate traffic is unchanged ──────────────────────────────────────────────────────

    [Fact]
    public void Same_tenant_update_and_delete_still_work()
    {
        var store = new InMemoryStore<Doc>();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        store.Create(new Doc { Guid = keep, TenantGuid = Attacker, TenantName = "Attacker", Name = "before" });
        store.Create(new Doc { Guid = drop, TenantGuid = Attacker, TenantName = "Attacker", Name = "doomed" });

        var wrapper = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));
        wrapper.Update(new Doc { Guid = keep, TenantGuid = Attacker, TenantName = "Attacker", Name = "after" });
        wrapper.Delete(new Doc { Guid = drop, TenantGuid = Attacker, TenantName = "Attacker" });

        store.Items[keep].Name.Should().Be("after");
        store.Items.Should().NotContainKey(drop);
    }

    [Fact]
    public void An_update_of_a_row_that_does_not_exist_is_still_refused_when_the_payload_claims_another_tenant()
    {
        // Nothing to authorize against, so the payload check remains — it is what stops an upserting
        // inner store from being made to create a row homed in another tenant.
        var wrapper = new TenantStoreWrapper<InMemoryStore<Doc>, Doc>(new InMemoryStore<Doc>(), CtxFor(Attacker, "Attacker"));

        wrapper.Invoking(w => w.Update(new Doc { Guid = Guid.NewGuid(), TenantGuid = Victim }))
            .Should().Throw<TenantMismatchException>();
    }

    [Fact]
    public void WithAllTenants_still_reaches_across_tenants()
    {
        var (store, rowGuid) = StoreWithVictimRow();
        var ctx = new TenantContext();
        var wrapper = new TenantStoreWrapper<InMemoryStore<Doc>, Doc>(store, ctx, TenantIsolationMode.Strict);

        ctx.WithAllTenants(() => wrapper.Update(new Doc
        {
            Guid = rowGuid,
            TenantGuid = Victim,
            TenantName = "Victim",
            Name = "admin-edit",
        }));

        store.Items[rowGuid].Name.Should().Be("admin-edit", "an explicit all-tenants scope is deliberate cross-tenant reach");
        store.Items[rowGuid].TenantGuid.Should().Be(Victim, "and the caller owns the per-item TenantGuid there");
    }

    [Fact]
    public void Permissive_with_no_tenant_in_scope_still_writes_across_tenants()
    {
        var (store, rowGuid) = StoreWithVictimRow();
        var wrapper = new TenantStoreWrapper<InMemoryStore<Doc>, Doc>(store, new TenantContext(), TenantIsolationMode.Permissive);

        wrapper.Invoking(w => w.Update(new Doc { Guid = rowGuid, TenantGuid = Victim, Name = "admin-edit" }))
            .Should().NotThrow("Permissive with no tenant is the documented non-tenant/admin mode");

        store.Items[rowGuid].Name.Should().Be("admin-edit");
    }

    // ── Batch behaviour ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_refused_item_anywhere_in_a_batch_leaves_the_whole_batch_unwritten_and_unstamped()
    {
        var store = new InMemoryStore<Doc>();
        var ours = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        store.Create(new Doc { Guid = ours, TenantGuid = Attacker, TenantName = "Attacker", Name = "ours" });
        store.Create(new Doc { Guid = theirs, TenantGuid = Victim, TenantName = "Victim", Name = "victim-data" });

        var wrapper = new TenantBulkStoreWrapper<InMemoryStore<Doc>, Doc>(store, CtxFor(Attacker, "Attacker"));
        // The legitimate item comes FIRST, so a per-item authorize-then-stamp loop would have already
        // re-stamped it by the time the second item is refused. Its TenantGuid is genuinely ours — a
        // foreign one would make the OLD payload guard throw on this item instead, which would let the
        // test pass without the fix and prove nothing. The stale TenantName is the stamping probe.
        var legitimate = new Doc { Guid = ours, TenantGuid = Attacker, TenantName = "stale-name", Name = "edited" };

        wrapper.Invoking(w => w.Update(new[] { legitimate, AttackPayload(theirs) }))
            .Should().Throw<TenantMismatchException>();

        store.Items[ours].Name.Should().Be("ours", "nothing in a refused batch is written");
        store.Items[theirs].Name.Should().Be("victim-data");
        legitimate.TenantName.Should().Be("stale-name", "authorization runs over the whole batch before any item is stamped");
    }

    /// <summary>Counts the reads the wrapper issues against the inner store.</summary>
    private sealed class CountingBulkStore : InMemoryStore<Doc>
    {
        public int SingleReads;
        public int BulkReads;

        public override Doc? Read(Expression<Func<Doc, bool>>? filter = null)
        {
            SingleReads++;
            // base.Read(filter) would bind to AbstractBulkStore's collection overload (the member-lookup
            // rule in CLAUDE.md § Conventions); ReadFirst is the single-result counterpart and forwards
            // to exactly the AbstractStore.Read body being overridden here.
            return base.ReadFirst(filter);
        }

        public override IEnumerable<Doc> Read(Expression<Func<Doc, bool>>? filter = null, OrderBy<Doc>? orderBy = null, int? limit = null, int? offset = null)
        {
            BulkReads++;
            return base.Read(filter, orderBy, limit, offset);
        }
    }

    [Fact]
    public void The_bulk_wrapper_resolves_a_whole_batch_in_one_read()
    {
        // The base wrapper reads per item; the bulk override collapses that to a single ModelsByGuid read,
        // so guarding a batch does not cost N round trips.
        var store = new CountingBulkStore();
        var guids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        foreach (var g in guids)
        {
            store.Create(new Doc { Guid = g, TenantGuid = Attacker, TenantName = "Attacker", Name = "row" });
        }

        var wrapper = new TenantBulkStoreWrapper<CountingBulkStore, Doc>(store, CtxFor(Attacker, "Attacker"));
        store.SingleReads = 0;
        store.BulkReads = 0;

        wrapper.Update(guids.Select(g => new Doc { Guid = g, TenantGuid = Attacker, Name = "edited" }).ToList());

        store.BulkReads.Should().Be(1, "one ModelsByGuid read covers the batch");
        store.SingleReads.Should().Be(0, "the per-item path must not be used by the bulk wrapper");
    }
}
