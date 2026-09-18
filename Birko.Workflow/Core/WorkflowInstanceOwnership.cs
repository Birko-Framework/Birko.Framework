using Birko.Workflow.Execution;

namespace Birko.Workflow.Core;

/// <summary>
/// The one producer for "does this persisted row belong to the workflow I was asked to save?".
/// </summary>
/// <remarks>
/// SH-H057. Every <see cref="IWorkflowInstanceStore{TData}"/> implementation upserts by
/// <c>InstanceId</c> alone and then assigned the supplied <c>workflowName</c> over whatever was
/// persisted. All seven backends share one table/collection across every workflow and every
/// <c>TData</c>, so a save aimed at the wrong row relabelled another workflow's instance and
/// overwrote its <c>DataJson</c>, <c>HistoryJson</c> and <c>CurrentState</c> — silently, because a
/// foreign payload deserialized through <c>ToInstance&lt;TData&gt;()</c> yields a defaulted
/// <c>TData</c> rather than throwing (System.Text.Json ignores unknown members).
///
/// The rule lives here rather than in seven copies: a rule with one statement and seven
/// implementations is the shape CLAUDE.md § Conventions keeps recording as the cause.
///
/// This supersedes CR-L404, which made the update branch refresh <c>WorkflowName</c> so a re-save
/// under a different name "isn't silently kept stale". With a mismatch refused, a stale name is not
/// reachable — the only way the persisted name and the requested name can differ is the defect this
/// guard exists to stop.
///
/// The refusal is deliberately total: an empty persisted name is refused too, rather than silently
/// adopted. Nothing writes an empty <c>WorkflowName</c> (<c>FromInstance</c> always sets it), so
/// adopting one would be a second rule covering a row that cannot exist. That is a decision, not an
/// oversight, and it is pinned by a test.
/// </remarks>
public static class WorkflowInstanceOwnership
{
    /// <summary>
    /// Throws <see cref="WorkflowInstanceOwnershipException"/> when <paramref name="persistedWorkflowName"/>
    /// names a different workflow from <paramref name="requestedWorkflowName"/>.
    /// </summary>
    /// <param name="persistedWorkflowName">The <c>WorkflowName</c> read back off the stored row.</param>
    /// <param name="requestedWorkflowName">The name the caller passed to <c>SaveAsync</c>.</param>
    /// <param name="instanceId">The instance being saved, for the message.</param>
    public static void RequireSameWorkflow(string? persistedWorkflowName, string requestedWorkflowName, Guid instanceId)
    {
        if (string.Equals(persistedWorkflowName, requestedWorkflowName, StringComparison.Ordinal))
        {
            return;
        }

        throw new WorkflowInstanceOwnershipException(requestedWorkflowName, persistedWorkflowName, instanceId);
    }
}
