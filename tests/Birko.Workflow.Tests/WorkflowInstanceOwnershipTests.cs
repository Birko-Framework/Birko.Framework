using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.Tests;

/// <summary>
/// SH-H057 — a save aimed at an instance id that belongs to a different workflow must be refused,
/// not silently relabelled and overwritten.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mechanism.</b> All seven <c>IWorkflowInstanceStore</c> backends share one table/collection
/// across every workflow and every <c>TData</c> (SQL: <c>__WorkflowInstances</c>), and every
/// <c>SaveAsync</c> upserted by <c>InstanceId</c> alone, then assigned the supplied
/// <c>workflowName</c> over whatever was persisted. So a save aimed at a foreign row relabelled it
/// and overwrote its <c>DataJson</c>, <c>HistoryJson</c> and <c>CurrentState</c>. Chained with
/// <c>SH-H056</c>'s unscoped <c>FindByState</c>/<c>FindByStatus</c> — which hand back foreign rows
/// deserialized as a *defaulted* <c>TData</c>, because System.Text.Json ignores unknown members —
/// the overwrite happens with default values and nothing throws. Silent cross-workflow data loss.
/// </para>
/// <para>
/// This file tests the producer; <c>JsonWorkflowInstanceStoreOwnershipTests</c> proves the observed
/// state end to end through a real store, and the source scan below proves all seven backends call it.
/// </para>
/// </remarks>
public class WorkflowInstanceOwnershipTests
{
    private const string NamesADoor =
        "a refusal that does not name a legitimate way forward gets worked around";

    [Fact]
    public void A_save_under_the_SAME_workflow_name_is_allowed()
    {
        var act = () => WorkflowInstanceOwnership.RequireSameWorkflow("OrderApproval", "OrderApproval", Guid.NewGuid());

        act.Should().NotThrow("an ordinary re-save of an instance must keep working — a false refusal "
                              + "here would break every update this contract exists to serve");
    }

    [Fact]
    public void A_save_under_a_DIFFERENT_workflow_name_is_refused_and_names_both_workflows()
    {
        var instanceId = Guid.NewGuid();

        var act = () => WorkflowInstanceOwnership.RequireSameWorkflow("InvoiceApproval", "OrderApproval", instanceId);

        var ex = act.Should().Throw<WorkflowInstanceOwnershipException>().Which;
        ex.PersistedWorkflowName.Should().Be("InvoiceApproval", "the caller has to be able to tell WHOSE row it nearly overwrote");
        ex.WorkflowName.Should().Be("OrderApproval");
        ex.InstanceId.Should().Be(instanceId);
        ex.Message.Should().Contain("InvoiceApproval").And.Contain("OrderApproval");
    }

    [Fact]
    public void The_refusal_names_the_doors_this_caller_actually_has()
    {
        // CLAUDE.md § SH-H037: a guard whose message only says "no" gets reached around. Both doors
        // exist on every backend — DeleteAsync, and the public Store property each one exposes.
        var act = () => WorkflowInstanceOwnership.RequireSameWorkflow("A", "B", Guid.NewGuid());

        var message = act.Should().Throw<WorkflowInstanceOwnershipException>().Which.Message;
        message.Should().Contain("delete", NamesADoor);
        message.Should().Contain("Store", NamesADoor);
    }

    [Fact]
    public void It_derives_from_WorkflowException_so_one_catch_selects_the_whole_family()
    {
        // The seven backends already throw WorkflowCompletedException / WorkflowFaultedException /
        // WorkflowActionException from this hierarchy; a host catching WorkflowException must see this too.
        typeof(WorkflowInstanceOwnershipException).Should().BeAssignableTo<WorkflowException>();
    }

    [Fact]
    public void An_EMPTY_persisted_name_is_refused_too_rather_than_silently_adopted()
    {
        // ⚠ DELIBERATE, not an oversight (CLAUDE.md § TASK-263: a gap that is a decision reads exactly
        // like an oversight unless you say so). Nothing writes an empty WorkflowName — FromInstance
        // always sets it — so adopting one would be a second rule covering a row that cannot exist.
        // Do not "fix" this into a silent adoption; that reopens the relabel this guard closes.
        var act = () => WorkflowInstanceOwnership.RequireSameWorkflow(string.Empty, "OrderApproval", Guid.NewGuid());

        act.Should().Throw<WorkflowInstanceOwnershipException>();
    }

    [Fact]
    public void A_NULL_persisted_name_is_refused_and_does_not_throw_a_NullReferenceException()
    {
        var act = () => WorkflowInstanceOwnership.RequireSameWorkflow(null, "OrderApproval", Guid.NewGuid());

        act.Should().Throw<WorkflowInstanceOwnershipException>()
           .Which.PersistedWorkflowName.Should().BeNull();
    }

    [Fact]
    public void The_comparison_is_ORDINAL_so_a_case_difference_is_a_different_workflow()
    {
        // Workflow names are matched exactly everywhere else in this library, and a store that treated
        // "orderapproval" as "OrderApproval" would be making a collation decision the rest of the
        // framework does not make.
        var act = () => WorkflowInstanceOwnership.RequireSameWorkflow("orderapproval", "OrderApproval", Guid.NewGuid());

        act.Should().Throw<WorkflowInstanceOwnershipException>();
    }

    /// <summary>
    /// ⚠ CLAUDE.md § TASK-243 — "a funnel with four overrides is not a funnel". The rule has ONE
    /// statement and SEVEN call sites, one per backend, in seven separate repos. Nothing in the type
    /// system makes a backend call it, and six of the seven cannot be reached offline, so this scan is
    /// the only thing that fails when a backend is added or edited without the guard.
    /// </summary>
    [Fact]
    public void Every_backend_SaveAsync_calls_the_guard_and_none_reassigns_WorkflowName()
    {
        var stores = BackendStoreSources();

        var unguarded = stores
            .Where(f => !File.ReadAllText(f).Contains("RequireSameWorkflow", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        unguarded.Should().BeEmpty("a backend whose SaveAsync does not call WorkflowInstanceOwnership "
                                   + "silently relabels and overwrites another workflow's instance row");

        // The dead reassignment the guard replaced. Restoring it is harmless only while the guard is
        // present, and it is exactly what a reader "restoring CR-L404" would put back — at which point
        // weakening the guard becomes invisible again.
        var reassigners = stores
            .Where(f => File.ReadAllText(f).Contains("WorkflowName = workflowName;", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        reassigners.Should().BeEmpty("with a mismatch refused the assignment is provably a no-op; it "
                                     + "supersedes CR-L404 rather than coexisting with it");
    }

    /// <summary>
    /// SH-H056 — the same cross-backend problem as the scan above, for the read side. Six of the seven
    /// backends cannot be exercised offline, so nothing else fails when one of them drops the scope.
    /// </summary>
    [Fact]
    public void Every_backend_scopes_FindByState_and_FindByStatus_to_the_requested_workflow()
    {
        var offenders = BackendStoreSources()
            .Select(f => new { Name = Path.GetFileName(f), Text = File.ReadAllText(f) })
            .Where(x => !x.Text.Contains("m.WorkflowName == workflowName && m.CurrentState == state", StringComparison.Ordinal)
                     || !x.Text.Contains("m.WorkflowName == workflowName && m.Status == statusInt", StringComparison.Ordinal))
            .Select(x => x.Name)
            .ToList();

        offenders.Should().BeEmpty(
            "an unscoped FindByState/FindByStatus returns other workflows' rows and deserializes them "
            + "into this store's TData with every member defaulted — no exception, no log entry");
    }

    /// <summary>
    /// CosmosDB used to hold the workflow name in a constructor field and AND it into these two
    /// queries, while its own <c>FindByWorkflowNameAsync</c> filtered on the parameter — two doors on
    /// one feature giving different answers (CLAUDE.md § TASK-274). The field is gone; this is what
    /// stops it, or an equivalent, coming back on any backend.
    /// </summary>
    [Fact]
    public void No_backend_holds_a_store_level_workflow_name()
    {
        var offenders = BackendStoreSources()
            .Where(f => File.ReadAllText(f).Contains("_workflowName", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty(
            "the workflow name is a per-call parameter on every operation that needs one — SaveAsync, "
            + "FindByState, FindByStatus, FindByWorkflowName — so a store-level copy is a second source "
            + "of truth that will disagree with one of them");
    }

    private static List<string> BackendStoreSources()
    {
        var stores = Directory
            .GetFiles(FrameworkRoot(), "*WorkflowInstanceStore.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !Path.GetFileName(f).StartsWith("I", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        stores.Should().HaveCount(7, "SQL, JSON, XML, MongoDB, RavenDB, ElasticSearch and CosmosDB each "
                                     + "carry their own copy of these methods — if this count moves, a "
                                     + "backend was added or removed and the new one needs the rules too");
        return stores;
    }

    private static string FrameworkRoot()
    {
        // .../Framework.Tests/Birko.Workflow.Tests/bin/Debug/net10.0 -> .../Framework
        // The tests live in a PARALLEL tree (CLAUDE.md § Testing), so the framework root is a sibling
        // of this repo, not an ancestor of it — check both shapes at every level rather than assuming.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (var candidate in new[] { dir.FullName, Path.Combine(dir.FullName, "Framework") })
            {
                if (Directory.Exists(Path.Combine(candidate, "Birko.Workflow.SQL")))
                {
                    return candidate;
                }
            }
        }

        throw new DirectoryNotFoundException(
            "The sibling Framework checkout was not findable from the test binary, and this scan is the "
            + "only cross-backend check there is — do not weaken it into a silent skip.");
    }
}
