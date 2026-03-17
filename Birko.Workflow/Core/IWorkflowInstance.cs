namespace Birko.Workflow.Core;

public interface IWorkflowInstance<TData>
{
    Guid InstanceId { get; }
    string CurrentState { get; }
    WorkflowStatus Status { get; }
    TData Data { get; }
    IReadOnlyList<StateChangeRecord> History { get; }
}
