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
    /// Finds instances of <paramref name="workflowName"/> that are in <paramref name="state"/>.
    /// </summary>
    /// <remarks>
    /// SH-H056. <paramref name="workflowName"/> is required and is not a convenience filter. Every
    /// backend keeps all workflows in one table/collection, so without it this query returned other
    /// workflows' rows and handed them back as <typeparamref name="TData"/> — which does not throw,
    /// because a foreign payload deserializes with every member defaulted. The result type can only
    /// be sound if the rows are restricted to the one workflow whose payload type is
    /// <typeparamref name="TData"/>, and a workflow name is the only thing that restricts them.
    /// </remarks>
    Task<IEnumerable<WorkflowInstance<TData>>> FindByStateAsync(string workflowName, string state, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds instances of <paramref name="workflowName"/> that are in <paramref name="status"/>.
    /// </summary>
    /// <remarks>See <see cref="FindByStateAsync"/> — <paramref name="workflowName"/> is required for the same reason (SH-H056).</remarks>
    Task<IEnumerable<WorkflowInstance<TData>>> FindByStatusAsync(string workflowName, WorkflowStatus status, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds workflow instances by workflow name.
    /// </summary>
    Task<IEnumerable<WorkflowInstance<TData>>> FindByWorkflowNameAsync(string workflowName, int limit = 100, CancellationToken cancellationToken = default);
}
