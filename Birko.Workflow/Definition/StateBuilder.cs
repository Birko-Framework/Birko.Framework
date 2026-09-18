using Birko.Workflow.Core;

namespace Birko.Workflow.Definition;

public sealed class StateBuilder<TData>
{
    private readonly WorkflowBuilder<TData> _parent;
    private readonly string _name;
    private string? _description;
    private bool _isFinal;
    private readonly List<Func<IWorkflowInstance<TData>, CancellationToken, Task>> _onEntryActions = new();
    private readonly List<Func<IWorkflowInstance<TData>, CancellationToken, Task>> _onExitActions = new();

    internal StateBuilder(WorkflowBuilder<TData> parent, string name)
    {
        _parent = parent;
        _name = name;
    }

    public StateBuilder<TData> Description(string description)
    {
        _description = description;
        return this;
    }

    public StateBuilder<TData> IsFinal()
    {
        _isFinal = true;
        return this;
    }

    public StateBuilder<TData> OnEntry(Func<IWorkflowInstance<TData>, CancellationToken, Task> action)
    {
        _onEntryActions.Add(action);
        return this;
    }

    public StateBuilder<TData> OnExit(Func<IWorkflowInstance<TData>, CancellationToken, Task> action)
    {
        _onExitActions.Add(action);
        return this;
    }

    public WorkflowBuilder<TData> And() => _parent;

    internal StateDefinition<TData> Build()
    {
        return new StateDefinition<TData>(
            _name,
            _description,
            _isFinal,
            _onEntryActions,
            _onExitActions);
    }
}
