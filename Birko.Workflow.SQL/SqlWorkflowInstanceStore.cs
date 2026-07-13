using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
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

        public SqlWorkflowInstanceStore(SqlSettings settings)
        {
            _store = new AsyncDataBaseBulkStore<DB, WorkflowInstanceModel>();
            _store.SetSettings(settings);
        }

        public SqlWorkflowInstanceStore(AsyncDataBaseBulkStore<DB, WorkflowInstanceModel> store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public AsyncDataBaseBulkStore<DB, WorkflowInstanceModel> Store => _store;

        /// <summary>
        /// Persists (upsert) a workflow instance. CR-M274: this is a best-effort read-then-write —
        /// callers MUST serialize saves per <c>InstanceId</c>. Two concurrent SaveAsync calls for the
        /// same instance can both observe <c>existing == null</c> and both CreateAsync; because Guid is a
        /// PrimaryField the second insert throws a primary-key violation (and an overlapping save can lose
        /// the first writer's changes). A native MERGE/ON-CONFLICT upsert would remove the race but is
        /// provider-specific; the per-instance serialization contract is the supported guarantee.
        /// </summary>
        public async Task<Guid> SaveAsync(string workflowName, WorkflowInstance<TData> instance, CancellationToken cancellationToken = default)
        {
            var existing = await _store.ReadFirstAsync(m => m.Guid == instance.InstanceId, cancellationToken).ConfigureAwait(false);
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
                orderBy: OrderBy<WorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
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
                orderBy: OrderBy<WorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }

        public async Task<IEnumerable<WorkflowInstance<TData>>> FindByWorkflowNameAsync(string workflowName, int limit = 100, CancellationToken cancellationToken = default)
        {
            var models = await _store.ReadAsync(
                filter: m => m.WorkflowName == workflowName,
                orderBy: OrderBy<WorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }
    }
}
