using Birko.Workflow.Execution;

namespace Birko.Workflow.Core;

/// <summary>
/// Persistence contract for workflow instances.
/// Definitions are not persisted — they contain Func delegates and are built in code via WorkflowBuilder.
/// </summary>
public interface IWorkflowInstanceStore<TData>
{
    /// <summary>
    /// Saves a workflow instance (create or update).
    /// </summary>
    Task<Guid> SaveAsync(string workflowName, WorkflowInstance<TData> instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a workflow instance by ID.
    /// </summary>
    Task<WorkflowInstance<TData>?> LoadAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a workflow instance.
    /// </summary>
    Task DeleteAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds workflow instances by current state.
    /// </summary>
    Task<IEnumerable<WorkflowInstance<TData>>> FindByStateAsync(string state, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds workflow instances by status.
    /// </summary>
    Task<IEnumerable<WorkflowInstance<TData>>> FindByStatusAsync(WorkflowStatus status, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds workflow instances by workflow name.
    /// </summary>
    Task<IEnumerable<WorkflowInstance<TData>>> FindByWorkflowNameAsync(string workflowName, int limit = 100, CancellationToken cancellationToken = default);
}
