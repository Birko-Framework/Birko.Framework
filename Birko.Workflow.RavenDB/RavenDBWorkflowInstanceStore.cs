using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.RavenDB.Models;

namespace Birko.Workflow.RavenDB
{
    public class RavenDBWorkflowInstanceStore<TData> : IWorkflowInstanceStore<TData>
        where TData : class
    {
        private readonly AsyncRavenDBStore<RavenWorkflowInstanceModel> _store;

        public RavenDBWorkflowInstanceStore(RemoteSettings settings)
        {
            _store = new AsyncRavenDBStore<RavenWorkflowInstanceModel>();
            _store.SetSettings(settings);
        }

        public RavenDBWorkflowInstanceStore(AsyncRavenDBStore<RavenWorkflowInstanceModel> store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public AsyncRavenDBStore<RavenWorkflowInstanceModel> Store => _store;

        public async Task<Guid> SaveAsync(string workflowName, WorkflowInstance<TData> instance, CancellationToken cancellationToken = default)
        {
            var existing = await _store.ReadAsync(m => m.Guid == instance.InstanceId, cancellationToken).ConfigureAwait(false);
            if (existing != null)
            {
                existing.UpdateFromInstance(instance);
                existing.WorkflowName = workflowName;
                await _store.UpdateAsync(existing, ct: cancellationToken).ConfigureAwait(false);
                return instance.InstanceId;
            }

            var model = RavenWorkflowInstanceModel.FromInstance(workflowName, instance);
            return await _store.CreateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        public async Task<WorkflowInstance<TData>?> LoadAsync(Guid instanceId, CancellationToken cancellationToken = default)
        {
            var model = await _store.ReadAsync(m => m.Guid == instanceId, cancellationToken).ConfigureAwait(false);
            return model?.ToInstance<TData>();
        }

        public async Task DeleteAsync(Guid instanceId, CancellationToken cancellationToken = default)
        {
            var model = await _store.ReadAsync(m => m.Guid == instanceId, cancellationToken).ConfigureAwait(false);
            if (model != null)
            {
                await _store.DeleteAsync(model, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IEnumerable<WorkflowInstance<TData>>> FindByStateAsync(string state, int limit = 100, CancellationToken cancellationToken = default)
        {
            var models = await _store.ReadAsync(
                filter: m => m.CurrentState == state,
                orderBy: OrderBy<RavenWorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }

        public async Task<IEnumerable<WorkflowInstance<TData>>> FindByStatusAsync(WorkflowStatus status, int limit = 100, CancellationToken cancellationToken = default)
        {
            var statusInt = (int)status;
            var models = await _store.ReadAsync(
                filter: m => m.Status == statusInt,
                orderBy: OrderBy<RavenWorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }

        public async Task<IEnumerable<WorkflowInstance<TData>>> FindByWorkflowNameAsync(string workflowName, int limit = 100, CancellationToken cancellationToken = default)
        {
            var models = await _store.ReadAsync(
                filter: m => m.WorkflowName == workflowName,
                orderBy: OrderBy<RavenWorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }
    }
}
