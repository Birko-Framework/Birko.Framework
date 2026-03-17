using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.JSON.Stores;
using Birko.Data.Stores;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.JSON.Models;

namespace Birko.Workflow.JSON
{
    /// <summary>
    /// JSON file-based workflow instance persistence.
    /// Good for development, testing, and single-process deployments.
    /// </summary>
    public class JsonWorkflowInstanceStore<TData> : IWorkflowInstanceStore<TData>
        where TData : class
    {
        private readonly AsyncJsonStore<JsonWorkflowInstanceModel> _store;
        private bool _initialized;

        public JsonWorkflowInstanceStore(Birko.Data.Stores.Settings settings)
        {
            _store = new AsyncJsonStore<JsonWorkflowInstanceModel>();
            _store.SetSettings(settings);
        }

        public JsonWorkflowInstanceStore(AsyncJsonStore<JsonWorkflowInstanceModel> store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public AsyncJsonStore<JsonWorkflowInstanceModel> Store => _store;

        public async Task<Guid> SaveAsync(string workflowName, WorkflowInstance<TData> instance, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var existing = await _store.ReadAsync(m => m.Guid == instance.InstanceId, cancellationToken).ConfigureAwait(false);
            if (existing != null)
            {
                existing.UpdateFromInstance(instance);
                existing.WorkflowName = workflowName;
                await _store.UpdateAsync(existing, ct: cancellationToken).ConfigureAwait(false);
                return instance.InstanceId;
            }

            var model = JsonWorkflowInstanceModel.FromInstance(workflowName, instance);
            return await _store.CreateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        public async Task<WorkflowInstance<TData>?> LoadAsync(Guid instanceId, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var model = await _store.ReadAsync(m => m.Guid == instanceId, cancellationToken).ConfigureAwait(false);
            return model?.ToInstance<TData>();
        }

        public async Task DeleteAsync(Guid instanceId, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var model = await _store.ReadAsync(m => m.Guid == instanceId, cancellationToken).ConfigureAwait(false);
            if (model != null)
            {
                await _store.DeleteAsync(model, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IEnumerable<WorkflowInstance<TData>>> FindByStateAsync(string state, int limit = 100, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var models = await _store.ReadAsync(
                filter: m => m.CurrentState == state,
                orderBy: OrderBy<JsonWorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }

        public async Task<IEnumerable<WorkflowInstance<TData>>> FindByStatusAsync(WorkflowStatus status, int limit = 100, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var statusInt = (int)status;
            var models = await _store.ReadAsync(
                filter: m => m.Status == statusInt,
                orderBy: OrderBy<JsonWorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }

        public async Task<IEnumerable<WorkflowInstance<TData>>> FindByWorkflowNameAsync(string workflowName, int limit = 100, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var models = await _store.ReadAsync(
                filter: m => m.WorkflowName == workflowName,
                orderBy: OrderBy<JsonWorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (_initialized) return;

            await _store.InitAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
    }
}
