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
