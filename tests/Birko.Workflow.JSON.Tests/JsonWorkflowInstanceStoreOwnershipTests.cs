using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.JSON.Stores;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.JSON.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.JSON.Tests;

/// <summary>
/// SH-H056 + SH-H057, end to end through a real store — the chain, and what it destroyed.
/// </summary>
/// <remarks>
/// <para>
/// Every backend shares one table/collection across every workflow and every <c>TData</c>, and
/// <c>SaveAsync</c> upserted by <c>InstanceId</c> alone before assigning the caller's
/// <c>workflowName</c> over whatever was persisted. So a save aimed at a foreign row relabelled it and
/// overwrote its payload, state and history. JSON is the offline-reachable member of the seven; the
/// producer is unit-tested in <c>Birko.Workflow.Tests</c> and the other six are covered by that
/// project's source scan, which is the only thing that fails when a backend skips the guard.
/// </para>
/// <para>
/// ⚠ <b>Every assertion here is observed state — the row read back — never "it did not throw".</b>
/// TASK-315's acceptance criterion demands it, and CLAUDE.md § Conventions records several defects a
/// did-not-throw assertion hid. The defect under test was silent by construction, so a test that only
/// asserted the exception would pass against a fix that threw *after* the overwrite.
/// </para>
/// </remarks>
public class JsonWorkflowInstanceStoreOwnershipTests : IDisposable
{
    private sealed class OrderData
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private sealed class InvoiceData
    {
        public string InvoiceNumber { get; set; } = string.Empty;
    }

    private readonly string _path;
    private readonly Settings _settings;

    public JsonWorkflowInstanceStoreOwnershipTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "birko-wf-ownership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_path);
        _settings = new Settings { Location = _path, Name = "workflow-instances" };
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leaked temp directory is TASK-302's subject, not this test's; do not fail a green run over it.
        }
    }

    private JsonWorkflowInstanceStore<T> StoreFor<T>() where T : class =>
        new JsonWorkflowInstanceStore<T>(_settings);

    private static WorkflowInstance<T> Instance<T>(Guid id, string state, T data) where T : class =>
        WorkflowInstance<T>.Restore(id, state, WorkflowStatus.Active, data, new List<StateChangeRecord>());

    /// <summary>
    /// Reads the raw persisted row, bypassing <c>ToInstance&lt;TData&gt;()</c>. That matters: the whole
    /// reason this was silent is that deserializing a foreign payload into the wrong <c>TData</c>
    /// succeeds with every property defaulted, so asking the store is asking the thing under test.
    /// </summary>
    private async Task<JsonWorkflowInstanceModel?> RawRowAsync(Guid id)
    {
        var raw = new AsyncJsonStore<JsonWorkflowInstanceModel>();
        raw.SetSettings(_settings);
        // CLAUDE.md § Conventions: on a bulk store the bulk Read(filter, ...) overload HIDES the
        // single-result one, so ReadAsync(filter) here would hand back the collection and
        // `Should().NotBeNull()` on it would assert nothing. ReadFirstAsync is the single-result door.
        return await raw.ReadFirstAsync(m => m.Guid == id);
    }

    [Fact]
    public async Task A_save_aimed_at_ANOTHER_workflows_instance_leaves_that_row_byte_for_byte_intact()
    {
        var invoiceStore = StoreFor<InvoiceData>();
        var invoiceId = Guid.NewGuid();
        await invoiceStore.SaveAsync("InvoiceApproval", Instance(invoiceId, "AwaitingSignature", new InvoiceData { InvoiceNumber = "INV-2026-0042" }));

        var before = await RawRowAsync(invoiceId);
        before.Should().NotBeNull();

        // The chained defect: a consumer holding an OrderApproval-typed store saves onto that id.
        var orderStore = StoreFor<OrderData>();
        var act = async () => await orderStore.SaveAsync(
            "OrderApproval",
            Instance(invoiceId, "Submitted", new OrderData { OrderId = "ORD-1", Amount = 99m }));

        await act.Should().ThrowAsync<WorkflowInstanceOwnershipException>();

        var after = await RawRowAsync(invoiceId);
        after.Should().NotBeNull();
        after!.WorkflowName.Should().Be("InvoiceApproval", "the row must not be relabelled");
        after.CurrentState.Should().Be("AwaitingSignature", "the foreign workflow's state must survive");
        after.DataJson.Should().Be(before!.DataJson, "this is the payload the defect overwrote with defaults");
        after.DataJson.Should().Contain("INV-2026-0042");
        after.HistoryJson.Should().Be(before.HistoryJson);
        after.UpdatedAt.Should().Be(before.UpdatedAt, "a refused save must not touch the row at all");
    }

    [Fact]
    public async Task The_refused_save_creates_NOTHING_either_so_the_store_still_holds_one_row()
    {
        var invoiceStore = StoreFor<InvoiceData>();
        var invoiceId = Guid.NewGuid();
        await invoiceStore.SaveAsync("InvoiceApproval", Instance(invoiceId, "AwaitingSignature", new InvoiceData { InvoiceNumber = "INV-1" }));

        var orderStore = StoreFor<OrderData>();
        try
        {
            await orderStore.SaveAsync("OrderApproval", Instance(invoiceId, "Submitted", new OrderData { OrderId = "ORD-1" }));
        }
        catch (WorkflowInstanceOwnershipException)
        {
            // expected
        }

        var raw = new AsyncJsonStore<JsonWorkflowInstanceModel>();
        raw.SetSettings(_settings);
        var rows = await raw.ReadAsync(m => m.WorkflowName != null);

        rows.Should().HaveCount(1, "refusing must not leave a half-written second row behind");
    }

    [Fact]
    public async Task An_ordinary_re_save_of_the_stores_OWN_instance_still_updates_it()
    {
        // ⚠ CONTRACT PIN, not evidence: this passes with or without the guard. It is here because a
        // guard that refused everything would also make the test above green, and that failure mode is
        // worse than the defect — CLAUDE.md § PredicateScope, "a false refusal breaks working code".
        var store = StoreFor<OrderData>();
        var id = Guid.NewGuid();
        await store.SaveAsync("OrderApproval", Instance(id, "Draft", new OrderData { OrderId = "ORD-7", Amount = 1m }));

        await store.SaveAsync("OrderApproval", Instance(id, "Submitted", new OrderData { OrderId = "ORD-7", Amount = 250m }));

        var row = await RawRowAsync(id);
        row.Should().NotBeNull();
        row!.CurrentState.Should().Be("Submitted");
        row.DataJson.Should().Contain("250");

        var restored = await store.LoadAsync(id);
        restored.Should().NotBeNull();
        restored!.Data.Amount.Should().Be(250m);
    }

    [Fact]
    public async Task SH_H056_FindByState_returns_only_ITS_OWN_workflows_rows()
    {
        // ⚠ INVERTED, not deleted. The first version of this test asserted the defect — that the read
        // still spanned workflows — because SH-H056 looked unfixable without an interface change.
        // TASK-315 then made that change (FindByState/FindByStatus take the workflow name, matching
        // SaveAsync and FindByWorkflowNameAsync), so the assertion moved rather than the scenario.
        // CLAUDE.md § TASK-211: a narrowing breaks the tests that asserted the wide behaviour, and
        // those are the interesting ones.
        var invoiceStore = StoreFor<InvoiceData>();
        await invoiceStore.SaveAsync("InvoiceApproval", Instance(Guid.NewGuid(), "Submitted", new InvoiceData { InvoiceNumber = "INV-9" }));

        var orderStore = StoreFor<OrderData>();
        var orderId = Guid.NewGuid();
        await orderStore.SaveAsync("OrderApproval", Instance(orderId, "Submitted", new OrderData { OrderId = "ORD-9", Amount = 5m }));

        var seen = (await orderStore.FindByStateAsync("OrderApproval", "Submitted")).ToList();

        seen.Should().ContainSingle("both rows are in state 'Submitted' and share one file, so an "
                                    + "unscoped query hands back the InvoiceApproval row too");
        seen[0].InstanceId.Should().Be(orderId);
        seen[0].Data.OrderId.Should().Be("ORD-9",
            "the foreign row used to arrive here as a DEFAULTED OrderData — no exception, every member "
            + "empty — which is exactly why this was silent");
    }

    [Fact]
    public async Task SH_H056_FindByStatus_is_scoped_the_same_way()
    {
        // Guard the whole verb family or none of it (CLAUDE.md § TASK-215): FindByStatus reaches the
        // identical unsound deserialization, so fixing only the filed FindByState would ship a store
        // whose two queries disagree about whose rows they return.
        var invoiceStore = StoreFor<InvoiceData>();
        await invoiceStore.SaveAsync("InvoiceApproval", Instance(Guid.NewGuid(), "AwaitingSignature", new InvoiceData { InvoiceNumber = "INV-8" }));

        var orderStore = StoreFor<OrderData>();
        var orderId = Guid.NewGuid();
        await orderStore.SaveAsync("OrderApproval", Instance(orderId, "Draft", new OrderData { OrderId = "ORD-8", Amount = 3m }));

        var seen = (await orderStore.FindByStatusAsync("OrderApproval", WorkflowStatus.Active)).ToList();

        seen.Should().ContainSingle("both rows are Active, so an unscoped query returns both");
        seen[0].InstanceId.Should().Be(orderId);
        seen[0].Data.OrderId.Should().Be("ORD-8");
    }

    [Fact]
    public async Task FindByWorkflowName_is_UNCHANGED_and_still_takes_its_name_from_the_caller()
    {
        // ⚠ CONTRACT PIN, green either way. It is here because CosmosDB used to AND this query with a
        // constructor-held _workflowName while filtering this one on the parameter — so its own two
        // doors disagreed. That field is gone; this asserts the surviving door was not "unified" into
        // the store-scoped shape by someone reasoning from symmetry.
        var store = StoreFor<OrderData>();
        var id = Guid.NewGuid();
        await store.SaveAsync("OrderApproval", Instance(id, "Draft", new OrderData { OrderId = "ORD-6" }));

        (await store.FindByWorkflowNameAsync("OrderApproval")).Should().ContainSingle();
        (await store.FindByWorkflowNameAsync("SomethingElse")).Should().BeEmpty();
    }

}
