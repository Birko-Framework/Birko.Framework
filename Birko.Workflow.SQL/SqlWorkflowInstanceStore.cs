using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.SQL.Models;

namespace Birko.Workflow.SQL
{
    /// <summary>
    /// SQL-based workflow instance persistence using Birko.Data.SQL stores.
    /// Works with any SQL connector (PostgreSQL, MSSql, MySQL, SQLite).
    /// </summary>
    public class SqlWorkflowInstanceStore<DB, TData> : IWorkflowInstanceStore<TData>
        where DB : AbstractConnector
        where TData : class
    {
        private readonly AsyncDataBaseBulkStore<DB, WorkflowInstanceModel> _store;
        private bool _initialized;

        public SqlWorkflowInstanceStore(PasswordSettings settings)
        {
            _store = new AsyncDataBaseBulkStore<DB, WorkflowInstanceModel>();
            _store.SetSettings(settings);
        }

        public SqlWorkflowInstanceStore(AsyncDataBaseBulkStore<DB, WorkflowInstanceModel> store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public AsyncDataBaseBulkStore<DB, WorkflowInstanceModel> Store => _store;

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

            var model = WorkflowInstanceModel.FromInstance(workflowName, instance);
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
                orderBy: OrderBy<WorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
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
                orderBy: OrderBy<WorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
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
                orderBy: OrderBy<WorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
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
