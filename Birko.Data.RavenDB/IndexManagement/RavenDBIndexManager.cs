using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Indexes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndexDefinition = Birko.Data.Patterns.IndexManagement.IndexDefinition;
using IndexField = Birko.Data.Patterns.IndexManagement.IndexField;
using IndexFieldType = Birko.Data.Patterns.IndexManagement.IndexFieldType;
using IndexInfo = Birko.Data.Patterns.IndexManagement.IndexInfo;
using IndexManagementException = Birko.Data.Patterns.IndexManagement.IndexManagementException;
using IIndexManager = Birko.Data.Patterns.IndexManagement.IIndexManager;

namespace Birko.Data.RavenDB.IndexManagement
{
    /// <summary>
    /// RavenDB implementation of <see cref="IIndexManager"/>.
    /// Scope is ignored — RavenDB indexes are database-wide.
    /// </summary>
    public class RavenDBIndexManager : IIndexManager
    {
        private readonly IDocumentStore _documentStore;

        public RavenDBIndexManager(IDocumentStore documentStore)
        {
            _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string indexName, string? scope = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            var indexNames = await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                .SendAsync(new GetIndexNamesOperation(0, int.MaxValue), ct).ConfigureAwait(false);

            return indexNames?.Any(n => string.Equals(n, indexName, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

        /// <inheritdoc />
        public async Task CreateAsync(IndexDefinition definition, string? scope = null, CancellationToken ct = default)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Name)) throw new ArgumentException("Index name is required.", nameof(definition));

            // Build a RavenDB IndexDefinition
            var ravenDef = new Raven.Client.Documents.Indexes.IndexDefinition
            {
                Name = definition.Name
            };

            // If a map expression was provided in properties, use it
            if (definition.Properties != null && definition.Properties.TryGetValue("Map", out var mapObj) && mapObj is string mapExpr)
            {
                ravenDef.Maps.Add(mapExpr);
            }
            else if (definition.Properties != null && definition.Properties.TryGetValue("Maps", out var mapsObj) && mapsObj is IEnumerable<string> maps)
            {
                foreach (var map in maps)
                {
                    ravenDef.Maps.Add(map);
                }
            }
            else if (definition.Fields.Count > 0)
            {
                // Auto-generate a simple map from field names + scope (collection name)
                var collectionName = scope ?? "docs";
                var fieldSelections = string.Join(", ", definition.Fields.Select(f =>
                    $"{f.Name} = doc.{f.Name}"));
                ravenDef.Maps.Add($"from doc in docs.{collectionName} select new {{ {fieldSelections} }}");
            }

            // Reduce expression
            if (definition.Properties != null && definition.Properties.TryGetValue("Reduce", out var reduceObj) && reduceObj is string reduceExpr)
            {
                ravenDef.Reduce = reduceExpr;
            }

            // Field options (sorting, analyzers)
            if (definition.Fields.Count > 0)
            {
                foreach (var field in definition.Fields)
                {
                    if (field.FieldType == IndexFieldType.Text)
                    {
                        ravenDef.Fields[field.Name] = new IndexFieldOptions
                        {
                            Indexing = FieldIndexing.Search
                        };
                    }
                }
            }

            try
            {
                await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                    .SendAsync(new PutIndexesOperation(ravenDef), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new IndexManagementException(
                    $"Failed to create index '{definition.Name}'.",
                    definition.Name, scope, ex);
            }
        }

        /// <inheritdoc />
        public async Task DropAsync(string indexName, string? scope = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            try
            {
                await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                    .SendAsync(new DeleteIndexOperation(indexName), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new IndexManagementException(
                    $"Failed to drop index '{indexName}'.",
                    indexName, scope, ex);
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<IndexInfo>> ListAsync(string? scope = null, CancellationToken ct = default)
        {
            var stats = await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                .SendAsync(new GetIndexesStatisticsOperation(), ct).ConfigureAwait(false);

            if (stats == null)
                return Array.Empty<IndexInfo>();

            return stats.Select(s => ToIndexInfo(s)).ToList();
        }

        /// <inheritdoc />
        public async Task<IndexInfo?> GetInfoAsync(string indexName, string? scope = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            var stats = await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                .SendAsync(new GetIndexStatisticsOperation(indexName), ct).ConfigureAwait(false);

            if (stats == null) return null;

            return ToIndexInfo(stats);
        }

        #region RavenDB-specific extensions

        /// <summary>
        /// Creates an index from a strongly-typed <see cref="AbstractIndexCreationTask"/>.
        /// </summary>
        public async Task CreateFromTaskAsync<TIndex>(CancellationToken ct = default)
            where TIndex : AbstractIndexCreationTask, new()
        {
            var index = new TIndex();
            await index.ExecuteAsync(_documentStore, token: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Resets an index, forcing a full re-indexation from scratch.
        /// </summary>
        public async Task ResetAsync(string indexName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                .SendAsync(new ResetIndexOperation(indexName), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Enables an index that was previously disabled.
        /// </summary>
        public async Task EnableAsync(string indexName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                .SendAsync(new EnableIndexOperation(indexName), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Disables an index (stops indexing, queries still work but may be stale).
        /// </summary>
        public async Task DisableAsync(string indexName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                .SendAsync(new DisableIndexOperation(indexName, false), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the priority of an index.
        /// </summary>
        public async Task SetPriorityAsync(string indexName, IndexPriority priority, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                .SendAsync(new SetIndexesPriorityOperation(indexName, priority), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the list of stale indexes (those still catching up with changes).
        /// </summary>
        public async Task<IReadOnlyList<string>> GetStaleIndexesAsync(CancellationToken ct = default)
        {
            var stats = await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                .SendAsync(new GetIndexesStatisticsOperation(), ct).ConfigureAwait(false);

            if (stats == null) return Array.Empty<string>();

            return stats.Where(s => s.IsStale).Select(s => s.Name).ToList();
        }

        #endregion

        #region Helpers

        private static IndexInfo ToIndexInfo(IndexStats stats)
        {
            var info = new IndexInfo
            {
                Name = stats.Name,
                State = stats.IsStale ? "stale" : stats.State.ToString().ToLowerInvariant(),
                Properties = new Dictionary<string, object>
                {
                    ["Type"] = stats.Type.ToString(),
                    ["Priority"] = stats.Priority.ToString(),
                    ["EntriesCount"] = stats.EntriesCount,
                    ["IsStale"] = stats.IsStale
                }
            };

            if (stats.MapAttempts > 0)
                info.Properties["MapAttempts"] = stats.MapAttempts;
            if (stats.MapSuccesses > 0)
                info.Properties["MapSuccesses"] = stats.MapSuccesses;
            if (stats.MapErrors > 0)
                info.Properties["MapErrors"] = stats.MapErrors;
            if (stats.ReduceAttempts.HasValue)
                info.Properties["ReduceAttempts"] = stats.ReduceAttempts.Value;

            return info;
        }

        #endregion
    }
}
