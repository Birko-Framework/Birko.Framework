using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.AI.Resilience.Data.Models;
using Birko.AI.Resilience.Stores;
using Birko.Data.Stores;

namespace Birko.AI.Resilience.Data.Stores
{
    /// <summary>
    /// <see cref="IUsageRepository"/> over any <see cref="IAsyncBulkStore{T}"/> that aggregates (TASK-530). Every total
    /// is one <see cref="IAsyncAggregatableStore{T}.AggregateAsync"/> call, so the store sums the rows; none are read
    /// into memory.
    /// </summary>
    /// <remarks>
    /// A store that cannot aggregate is refused at construction: summing in memory would read every usage row for
    /// each budget check, which is the cost this type exists to avoid. Every SQL provider, InMemory and the
    /// <c>StoreWrapperBuilder</c> decorators aggregate.
    /// </remarks>
    public class StoreUsageRepository : IUsageRepository
    {
        private const string Requests = "requests";
        private const string Tokens = "tokens";
        private const string Cost = "cost";

        private static readonly IReadOnlyList<AggregateField> Totals =
        [
            new(AggregateFunction.Count, nameof(UsageRecordModel.Guid), Requests),
            new(AggregateFunction.Sum, nameof(UsageRecordModel.TotalTokens), Tokens),
            new(AggregateFunction.Sum, nameof(UsageRecordModel.EstimatedCostUsd), Cost),
        ];

        private readonly IAsyncBulkStore<UsageRecordModel> _store;
        private readonly IAsyncAggregatableStore<UsageRecordModel> _aggregator;

        /// <exception cref="ArgumentException">The store does not implement <see cref="IAsyncAggregatableStore{T}"/>.</exception>
        public StoreUsageRepository(IAsyncBulkStore<UsageRecordModel> store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _aggregator = store as IAsyncAggregatableStore<UsageRecordModel>
                ?? throw new ArgumentException(
                    $"{store.GetType().Name} does not aggregate (IAsyncAggregatableStore<{nameof(UsageRecordModel)}>), so usage totals would have to be summed in memory.",
                    nameof(store));
        }

        public Task RecordUsageAsync(UsageRecordEntity entity, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return _store.CreateAsync(UsageRecordModel.FromEntity(entity), ct: ct);
        }

        public async Task<double> GetTotalSpendAsync(DateTime from, DateTime to, string? provider = null, CancellationToken ct = default)
        {
            Expression<Func<UsageRecordModel, bool>> filter = provider == null
                ? x => x.RecordedAt >= from && x.RecordedAt < to
                : x => x.RecordedAt >= from && x.RecordedAt < to && x.Provider == provider;
            var rows = await AggregateAsync(filter, groupBy: null, ct).ConfigureAwait(false);
            return rows.Count == 0 ? 0 : rows[0].GetValue<double>(Cost);
        }

        public async Task<double> GetProjectSpendAsync(string projectId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(projectId);
            var rows = await AggregateAsync(x => x.ProjectId == projectId, groupBy: null, ct).ConfigureAwait(false);
            return rows.Count == 0 ? 0 : rows[0].GetValue<double>(Cost);
        }

        public async Task<List<ProviderUsageSummary>> GetUsageByProviderAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            var rows = await AggregateAsync(x => x.RecordedAt >= from && x.RecordedAt < to,
                groupBy: nameof(UsageRecordModel.Provider), ct).ConfigureAwait(false);
            return rows
                .Select(r => new ProviderUsageSummary
                {
                    Provider = r.GetValue<string>(nameof(UsageRecordModel.Provider)) ?? string.Empty,
                    TotalRequests = r.GetValue<int>(Requests),
                    TotalTokens = r.GetValue<int>(Tokens),
                    TotalCostUsd = r.GetValue<double>(Cost),
                })
                .OrderBy(s => s.Provider, StringComparer.Ordinal)
                .ToList();
        }

        /// <returns>The project's totals in the window, or <c>null</c> when it made no call in it.</returns>
        public async Task<ProjectUsageSummary?> GetUsageByProjectAsync(string projectId, DateTime from, DateTime to, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(projectId);
            var rows = await AggregateAsync(x => x.ProjectId == projectId && x.RecordedAt >= from && x.RecordedAt < to,
                groupBy: null, ct).ConfigureAwait(false);
            var requests = rows.Count == 0 ? 0 : rows[0].GetValue<int>(Requests);
            if (requests == 0) return null;
            return new ProjectUsageSummary
            {
                ProjectId = projectId,
                TotalRequests = requests,
                TotalTokens = rows[0].GetValue<int>(Tokens),
                TotalCostUsd = rows[0].GetValue<double>(Cost),
            };
        }

        private Task<IReadOnlyList<AggregateResult>> AggregateAsync(
            Expression<Func<UsageRecordModel, bool>> filter, string? groupBy, CancellationToken ct)
            => _aggregator.AggregateAsync(new AggregateQuery<UsageRecordModel>
            {
                Filter = filter,
                GroupByFields = groupBy == null ? [] : [groupBy],
                Aggregates = Totals,
            }, ct);
    }
}
