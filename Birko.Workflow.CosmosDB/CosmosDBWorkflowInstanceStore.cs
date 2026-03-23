using Birko.Workflow.CosmosDB.Models;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Workflow.CosmosDB;

/// <summary>
/// Cosmos DB implementation of IWorkflowInstanceStore using AsyncCosmosDBStore.
/// </summary>
/// <typeparam name="TData">The workflow data type.</typeparam>
public class CosmosDBWorkflowInstanceStore<TData> : IWorkflowInstanceStore<TData>
    where TData : class
{
    private readonly AsyncCosmosDBStore<CosmosWorkflowInstanceModel> _store;
    private readonly string _workflowName;
    private bool _initialized;

    /// <summary>
    /// Gets the underlying store for transaction context access.
    /// </summary>
    public AsyncCosmosDBStore<CosmosWorkflowInstanceModel> Store => _store;

    /// <summary>
    /// Creates a new Cosmos DB workflow instance store with settings.
    /// </summary>
    public CosmosDBWorkflowInstanceStore(string workflowName, RemoteSettings settings)
    {
        _workflowName = workflowName ?? throw new ArgumentNullException(nameof(workflowName));
        _store = new AsyncCosmosDBStore<CosmosWorkflowInstanceModel>();
        _store.SetSettings(settings);
    }

    /// <summary>
    /// Creates a new Cosmos DB workflow instance store with an existing store.
    /// </summary>
    public CosmosDBWorkflowInstanceStore(string workflowName, AsyncCosmosDBStore<CosmosWorkflowInstanceModel> store)
    {
        _workflowName = workflowName ?? throw new ArgumentNullException(nameof(workflowName));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _initialized = true;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (!_initialized)
        {
            await _store.InitAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(string workflowName, WorkflowInstance<TData> instance, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var existing = await _store.ReadAsync(m => m.Guid == instance.InstanceId, ct).ConfigureAwait(false);

        if (existing != null)
        {
            existing.UpdateFromInstance(instance);
            await _store.UpdateAsync(existing, ct: ct).ConfigureAwait(false);
        }
        else
        {
            var model = CosmosWorkflowInstanceModel.FromInstance(workflowName, instance);
            await _store.CreateAsync(model, ct: ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowInstance<TData>?> LoadAsync(Guid instanceId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var model = await _store.ReadAsync(m => m.Guid == instanceId, ct).ConfigureAwait(false);
        return model?.ToInstance<TData>();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid instanceId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var model = await _store.ReadAsync(m => m.Guid == instanceId, ct).ConfigureAwait(false);
        if (model != null)
        {
            await _store.DeleteAsync(model, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowInstance<TData>>> FindByStateAsync(string state, int? limit = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var results = await _store.ReadAsync(
            filter: m => m.CurrentState == state && m.WorkflowName == _workflowName,
            orderBy: new OrderBy<CosmosWorkflowInstanceModel>(nameof(CosmosWorkflowInstanceModel.UpdatedAt), true),
            limit: limit,
            ct: ct
        ).ConfigureAwait(false);

        return results.Select(m => m.ToInstance<TData>()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowInstance<TData>>> FindByStatusAsync(WorkflowStatus status, int? limit = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var statusInt = (int)status;
        var results = await _store.ReadAsync(
            filter: m => m.Status == statusInt && m.WorkflowName == _workflowName,
            orderBy: new OrderBy<CosmosWorkflowInstanceModel>(nameof(CosmosWorkflowInstanceModel.UpdatedAt), true),
            limit: limit,
            ct: ct
        ).ConfigureAwait(false);

        return results.Select(m => m.ToInstance<TData>()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowInstance<TData>>> FindByWorkflowNameAsync(string workflowName, int? limit = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var results = await _store.ReadAsync(
            filter: m => m.WorkflowName == workflowName,
            orderBy: new OrderBy<CosmosWorkflowInstanceModel>(nameof(CosmosWorkflowInstanceModel.UpdatedAt), true),
            limit: limit,
            ct: ct
        ).ConfigureAwait(false);

        return results.Select(m => m.ToInstance<TData>()).ToList();
    }
}
