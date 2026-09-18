using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Workflow.Core;
using Birko.Workflow.ElasticSearch.Models;
using Birko.Workflow.Execution;

namespace Birko.Workflow.ElasticSearch
{
    public class ElasticSearchWorkflowInstanceStore<TData> : IWorkflowInstanceStore<TData>
        where TData : class
    {
        private readonly AsyncElasticSearchStore<ElasticWorkflowInstanceModel> _store;

        public ElasticSearchWorkflowInstanceStore(Birko.Data.ElasticSearch.Stores.Settings settings)
        {
            _store = new AsyncElasticSearchStore<ElasticWorkflowInstanceModel>();
            _store.SetSettings(settings);
        }

        public ElasticSearchWorkflowInstanceStore(AsyncElasticSearchStore<ElasticWorkflowInstanceModel> store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public AsyncElasticSearchStore<ElasticWorkflowInstanceModel> Store => _store;

        /// <remarks>
        /// CR-L407: this is a read-then-write upsert (Read → branch to Update/Create), not an atomic
        /// operation. Two concurrent SaveAsync calls for the same new InstanceId can both observe
        /// existing == null and both Create, producing duplicate ES documents (CreateAsync mints its
        /// own _id rather than keying on Guid, so the duplicate is silent). This at-least-once /
        /// possible-duplicate characteristic is shared with the MongoDB/RavenDB/CosmosDB siblings; for
        /// exactly-once upsert, key the ES _id off InstanceId or use optimistic concurrency.
        /// </remarks>
        public async Task<Guid> SaveAsync(string workflowName, WorkflowInstance<TData> instance, CancellationToken cancellationToken = default)
        {
            var existing = await _store.ReadAsync(m => m.Guid == instance.InstanceId, cancellationToken).ConfigureAwait(false);
            if (existing != null)
            {
                // SH-H057: the row is keyed by InstanceId alone and every workflow shares one
                // table/collection, so an id identifies a row, not a workflow. Refuse before
                // UpdateFromInstance relabels and overwrites another workflow's instance.
                WorkflowInstanceOwnership.RequireSameWorkflow(existing.WorkflowName, workflowName, instance.InstanceId);
                existing.UpdateFromInstance(instance);
                await _store.UpdateAsync(existing, ct: cancellationToken).ConfigureAwait(false);
                return instance.InstanceId;
            }

            var model = ElasticWorkflowInstanceModel.FromInstance(workflowName, instance);
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

        public async Task<IEnumerable<WorkflowInstance<TData>>> FindByStateAsync(string workflowName, string state, int limit = 100, CancellationToken cancellationToken = default)
        {
            var models = await _store.ReadAsync(
                filter: m => m.WorkflowName == workflowName && m.CurrentState == state,
                orderBy: OrderBy<ElasticWorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }

        public async Task<IEnumerable<WorkflowInstance<TData>>> FindByStatusAsync(string workflowName, WorkflowStatus status, int limit = 100, CancellationToken cancellationToken = default)
        {
            var statusInt = (int)status;
            var models = await _store.ReadAsync(
                filter: m => m.WorkflowName == workflowName && m.Status == statusInt,
                orderBy: OrderBy<ElasticWorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }

        public async Task<IEnumerable<WorkflowInstance<TData>>> FindByWorkflowNameAsync(string workflowName, int limit = 100, CancellationToken cancellationToken = default)
        {
            var models = await _store.ReadAsync(
                filter: m => m.WorkflowName == workflowName,
                orderBy: OrderBy<ElasticWorkflowInstanceModel>.ByDescending(m => m.UpdatedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToInstance<TData>());
        }
    }
}
