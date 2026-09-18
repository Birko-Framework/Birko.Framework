using Birko.Workflow.Core;

namespace Birko.Workflow.Definition;

public sealed class TransitionDefinition<TData>
{
    public string Trigger { get; }
    public string FromState { get; }
    public string ToState { get; }
    public IReadOnlyList<(Func<IWorkflowInstance<TData>, bool> Predicate, string Reason)> Guards { get; }
    public IReadOnlyList<Func<IWorkflowInstance<TData>, CancellationToken, Task>> Actions { get; }

    internal TransitionDefinition(
        string trigger,
        string fromState,
        string toState,
        IReadOnlyList<(Func<IWorkflowInstance<TData>, bool> Predicate, string Reason)> guards,
        IReadOnlyList<Func<IWorkflowInstance<TData>, CancellationToken, Task>> actions)
    {
        Trigger = trigger;
        FromState = fromState;
        ToState = toState;
        Guards = guards;
        Actions = actions;
    }
}
