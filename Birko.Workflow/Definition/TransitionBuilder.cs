using Birko.Workflow.Core;

namespace Birko.Workflow.Definition;

public sealed class TransitionBuilder<TData>
{
    private readonly WorkflowBuilder<TData> _parent;
    private readonly string _trigger;
    private readonly string _fromState;
    private readonly string _toState;
    private readonly List<(Func<IWorkflowInstance<TData>, bool> Predicate, string Reason)> _guards = new();
    private readonly List<Func<IWorkflowInstance<TData>, CancellationToken, Task>> _actions = new();

    internal TransitionBuilder(WorkflowBuilder<TData> parent, string trigger, string fromState, string toState)
    {
        _parent = parent;
        _trigger = trigger;
        _fromState = fromState;
        _toState = toState;
    }

    public TransitionBuilder<TData> Guard(Func<IWorkflowInstance<TData>, bool> predicate, string reason = "Guard failed")
    {
        _guards.Add((predicate, reason));
        return this;
    }

    public TransitionBuilder<TData> Action(Func<IWorkflowInstance<TData>, CancellationToken, Task> action)
    {
        _actions.Add(action);
        return this;
    }

    public WorkflowBuilder<TData> And() => _parent;

    internal TransitionDefinition<TData> Build()
    {
        return new TransitionDefinition<TData>(
            _trigger,
            _fromState,
            _toState,
            _guards,
            _actions);
    }
}
