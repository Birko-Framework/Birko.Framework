using Birko.Workflow.Definition;

namespace Birko.Workflow.Core;

public interface IWorkflowDefinition<TData>
{
    string Name { get; }
    string InitialState { get; }
    IReadOnlyList<StateDefinition<TData>> States { get; }
    IReadOnlyList<TransitionDefinition<TData>> Transitions { get; }
    IReadOnlyList<string> GetPermittedTriggers(string state);
}
