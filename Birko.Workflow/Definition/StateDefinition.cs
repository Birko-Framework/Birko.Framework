using Birko.Workflow.Core;

namespace Birko.Workflow.Definition;

public sealed class StateDefinition<TData>
{
    public string Name { get; }
    public string? Description { get; }
    public bool IsFinal { get; }
    public IReadOnlyList<Func<IWorkflowInstance<TData>, CancellationToken, Task>> OnEntryActions { get; }
    public IReadOnlyList<Func<IWorkflowInstance<TData>, CancellationToken, Task>> OnExitActions { get; }

    internal StateDefinition(
        string name,
        string? description,
        bool isFinal,
        IReadOnlyList<Func<IWorkflowInstance<TData>, CancellationToken, Task>> onEntryActions,
        IReadOnlyList<Func<IWorkflowInstance<TData>, CancellationToken, Task>> onExitActions)
    {
        Name = name;
        Description = description;
        IsFinal = isFinal;
        OnEntryActions = onEntryActions;
        OnExitActions = onExitActions;
    }
}
