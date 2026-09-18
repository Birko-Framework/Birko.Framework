using Birko.Workflow.Core;

namespace Birko.Workflow.Definition;

public sealed class WorkflowDefinition<TData> : IWorkflowDefinition<TData>
{
    public string Name { get; }
    public string InitialState { get; }
    public IReadOnlyList<StateDefinition<TData>> States { get; }
    public IReadOnlyList<TransitionDefinition<TData>> Transitions { get; }

    internal WorkflowDefinition(
        string name,
        string initialState,
        IReadOnlyList<StateDefinition<TData>> states,
        IReadOnlyList<TransitionDefinition<TData>> transitions)
    {
        Name = name;
        InitialState = initialState;
        States = states;
        Transitions = transitions;
    }

    public IReadOnlyList<string> GetPermittedTriggers(string state)
    {
        return Transitions
            .Where(t => t.FromState == state)
            .Select(t => t.Trigger)
            .Distinct()
            .ToList();
    }
}
