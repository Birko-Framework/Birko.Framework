using Birko.Workflow.Core;

namespace Birko.Workflow.Execution;

public sealed class WorkflowInstance<TData> : IWorkflowInstance<TData>
{
    public Guid InstanceId { get; }
    public string CurrentState { get; internal set; }
    public WorkflowStatus Status { get; internal set; }
    public TData Data { get; }
    public IReadOnlyList<StateChangeRecord> History => _history;

    private readonly List<StateChangeRecord> _history = new();

    private WorkflowInstance(Guid instanceId, string currentState, WorkflowStatus status, TData data)
    {
        InstanceId = instanceId;
        CurrentState = currentState;
        Status = status;
        Data = data;
    }

    public static WorkflowInstance<TData> Create(IWorkflowDefinition<TData> definition, TData data)
    {
        return new WorkflowInstance<TData>(
            Guid.NewGuid(),
            definition.InitialState,
            WorkflowStatus.Active,
            data);
    }

    public static WorkflowInstance<TData> Restore(
        Guid instanceId,
        string currentState,
        WorkflowStatus status,
        TData data,
        IEnumerable<StateChangeRecord>? history = null)
    {
        var instance = new WorkflowInstance<TData>(instanceId, currentState, status, data);
        if (history != null)
        {
            instance._history.AddRange(history);
        }
        return instance;
    }

    internal void AddHistoryRecord(StateChangeRecord record)
    {
        _history.Add(record);
    }
}
