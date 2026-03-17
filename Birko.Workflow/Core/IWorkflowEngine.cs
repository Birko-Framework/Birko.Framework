using Birko.Workflow.Execution;

namespace Birko.Workflow.Core;

public interface IWorkflowEngine
{
    Task<TransitionResult> FireAsync<TData>(
        IWorkflowDefinition<TData> definition,
        IWorkflowInstance<TData> instance,
        string trigger,
        CancellationToken cancellationToken = default);

    IReadOnlyList<string> GetPermittedTriggers<TData>(
        IWorkflowDefinition<TData> definition,
        IWorkflowInstance<TData> instance);
}
