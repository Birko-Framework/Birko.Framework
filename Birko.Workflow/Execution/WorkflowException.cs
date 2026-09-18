namespace Birko.Workflow.Execution;

public class WorkflowException : Exception
{
    public string WorkflowName { get; }
    public Guid InstanceId { get; }

    public WorkflowException(string workflowName, Guid instanceId, string message)
        : base(message)
    {
        WorkflowName = workflowName;
        InstanceId = instanceId;
    }

    public WorkflowException(string workflowName, Guid instanceId, string message, Exception innerException)
        : base(message, innerException)
    {
        WorkflowName = workflowName;
        InstanceId = instanceId;
    }
}

public sealed class WorkflowCompletedException : WorkflowException
{
    public WorkflowCompletedException(string workflowName, Guid instanceId)
        : base(workflowName, instanceId, $"Workflow '{workflowName}' instance '{instanceId}' is already completed and cannot accept new triggers.")
    {
    }
}

public sealed class WorkflowFaultedException : WorkflowException
{
    public WorkflowFaultedException(string workflowName, Guid instanceId)
        : base(workflowName, instanceId, $"Workflow '{workflowName}' instance '{instanceId}' is faulted and cannot accept new triggers.")
    {
    }
}

public sealed class WorkflowActionException : WorkflowException
{
    public string State { get; }
    public string Trigger { get; }

    public WorkflowActionException(string workflowName, Guid instanceId, string state, string trigger, Exception innerException)
        : base(workflowName, instanceId, $"Action failed during transition '{trigger}' in state '{state}' of workflow '{workflowName}'.", innerException)
    {
        State = state;
        Trigger = trigger;
    }
}

/// <summary>
/// SH-H057: a save was aimed at an instance row that belongs to a different workflow.
/// </summary>
/// <remarks>
/// Raised by <see cref="Birko.Workflow.Core.WorkflowInstanceOwnership.RequireSameWorkflow"/> before
/// the update branch of any backend's <c>SaveAsync</c> can relabel and overwrite the foreign row.
/// The message names both doors a caller actually has, because a guard that only says "no" gets
/// reached around: delete the instance and save it afresh under the new name, or relabel the row
/// directly through the backend store's own <c>Store</c> property, which every implementation exposes.
/// </remarks>
public sealed class WorkflowInstanceOwnershipException : WorkflowException
{
    /// <summary>The <c>WorkflowName</c> found on the stored row.</summary>
    public string? PersistedWorkflowName { get; }

    public WorkflowInstanceOwnershipException(string workflowName, string? persistedWorkflowName, Guid instanceId)
        : base(workflowName, instanceId,
            $"Workflow instance '{instanceId}' belongs to workflow '{persistedWorkflowName}', not '{workflowName}', "
            + "so saving it here would overwrite another workflow's data. All backends share one "
            + "table/collection across workflows, so an instance id identifies a row, not a workflow. "
            + "To move an instance deliberately, delete it and save it afresh under the new name, or "
            + "relabel the stored row through the backend store's own Store property.")
    {
        PersistedWorkflowName = persistedWorkflowName;
    }
}
