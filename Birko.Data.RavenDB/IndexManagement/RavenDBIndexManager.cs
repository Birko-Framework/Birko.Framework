using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations.Indexes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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

            // SH-M432 (TASK-154): these flags were ignored, so a definition asking for a unique, sparse or expiring index
            // created a plain one and reported success (rule 55). A RavenDB index enforces none of them.
            if (definition.Unique)
                throw new NotSupportedException($"RavenDB indexes are not unique constraints; '{definition.Name}' cannot be created Unique. Use compare-exchange values for uniqueness.");
            if (definition.Sparse)
                throw new NotSupportedException($"RavenDB has no sparse index; '{definition.Name}' cannot be created Sparse.");
            if (definition.ExpireAfter.HasValue)
                throw new NotSupportedException($"A RavenDB index does not expire documents; '{definition.Name}' cannot take ExpireAfter. Use the @expires metadata with document expiration enabled.");

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
                // SH-M432 (TASK-154): the scope and field names are interpolated into map CODE that the server compiles,
                // so they are held to plain identifiers rather than escaped (rule 33, containment by refusal). A map for
                // anything else is written by the caller and passed as the "Map" property.
                var collectionName = RequireMapIdentifier(scope ?? "docs", "collection", definition.Name);
                ravenDef.Maps.Add(GeneratedMap(collectionName,
                    definition.Fields.Select(f => RequireMapIdentifier(f.Name, "field", definition.Name))));
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

            // TASK-635, measured on RavenDB 6: statistics of an index that does not exist throw InvalidOperationException
            // ("Response is invalid."), so the null check below never ran and an absent index threw instead of answering
            // null, as IIndexManager.GetInfoAsync promises.
            if (!await ExistsAsync(indexName, scope, ct).ConfigureAwait(false)) return null;

            var stats = await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                .SendAsync(new GetIndexStatisticsOperation(indexName), ct).ConfigureAwait(false);

            if (stats == null) return null;

            var info = ToIndexInfo(stats);

            // TASK-635: report the fields of a map this manager generated, so drift detection can compare them. A map the
            // caller wrote is not parsed: its fields stay empty, which the detector reports as not compared.
            var definition = await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
                .SendAsync(new GetIndexOperation(indexName), ct).ConfigureAwait(false);
            if (definition != null && definition.Maps.Count == 1 && FieldsOfGeneratedMap(definition.Maps.First()) is { } fields)
            {
                info.Fields = fields.Select(f => new IndexField
                {
                    Name = f,
                    FieldType = definition.Fields.TryGetValue(f, out var options) && options.Indexing == FieldIndexing.Search
                        ? IndexFieldType.Text
                        : IndexFieldType.Standard
                }).ToList();
            }
            return info;
        }

        /// <summary>The map <see cref="CreateAsync"/> generates for plain fields: the one producer of that text.</summary>
        internal static string GeneratedMap(string collection, IEnumerable<string> fields)
            => $"from doc in docs.{collection} select new {{ {string.Join(", ", fields.Select(f => $"{f} = doc.{f}"))} }}";

        private static readonly System.Text.RegularExpressions.Regex GeneratedMapShape = new(
            @"^from doc in docs\.(?<c>[A-Za-z_][A-Za-z0-9_]*) select new \{ (?<f>[A-Za-z_][A-Za-z0-9_]*) = doc\.\k<f>(, (?<f>[A-Za-z_][A-Za-z0-9_]*) = doc\.\k<f>)* \}$");

        /// <summary>
        /// The fields of a map in exactly the form <see cref="GeneratedMap"/> writes, or null for any other map.
        /// </summary>
        internal static IReadOnlyList<string>? FieldsOfGeneratedMap(string map)
        {
            var match = GeneratedMapShape.Match(map.Trim());
            if (!match.Success) return null;
            var fields = match.Groups["f"].Captures.Select(c => c.Value).ToList();
            return GeneratedMap(match.Groups["c"].Value, fields) == map.Trim() ? fields : null;
        }

        private static readonly System.Text.RegularExpressions.Regex MapIdentifier = new("^[A-Za-z_][A-Za-z0-9_]*$");

        private static string RequireMapIdentifier(string name, string what, string index)
            => MapIdentifier.IsMatch(name)
                ? name
                : throw new ArgumentException(
                    $"Index '{index}': the {what} name '{name}' is not a plain identifier, so it cannot be put into a generated "
                    + "map. Pass the map yourself in the \"Map\" property.", nameof(index));

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

        /// <summary>
        /// Deploys all <see cref="AbstractIndexCreationTask"/> implementations found in the specified assembly.
        /// </summary>
        /// <param name="assembly">The assembly to scan for index creation tasks.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>List of deployed index names.</returns>
        public async Task<IReadOnlyList<string>> DeployFromAssemblyAsync(Assembly assembly, CancellationToken ct = default)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            var indexTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(AbstractIndexCreationTask).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                .ToList();

            var deployed = new List<string>();

            foreach (var type in indexTypes)
            {
                var index = (AbstractIndexCreationTask)Activator.CreateInstance(type)!;
                try
                {
                    await index.ExecuteAsync(_documentStore, token: ct).ConfigureAwait(false);
                    deployed.Add(index.IndexName);
                }
                catch (Exception ex)
                {
                    throw new IndexManagementException(
                        $"Failed to deploy index '{index.IndexName}' from type '{type.FullName}'.",
                        index.IndexName, null, ex);
                }
            }

            return deployed;
        }

        /// <summary>
        /// Deploys all <see cref="AbstractIndexCreationTask"/> implementations found in the assembly containing <typeparamref name="TAssemblyMarker"/>.
        /// </summary>
        public Task<IReadOnlyList<string>> DeployFromAssemblyAsync<TAssemblyMarker>(CancellationToken ct = default)
        {
            return DeployFromAssemblyAsync(typeof(TAssemblyMarker).Assembly, ct);
        }

        #endregion

        #region Map/Reduce Query Helpers

        /// <summary>
        /// Queries a Map/Reduce index and returns typed results with optional filtering, ordering, and paging.
        /// </summary>
        /// <typeparam name="TResult">The result type matching the index's Reduce output shape.</typeparam>
        /// <param name="indexName">The name of the Map/Reduce index to query.</param>
        /// <param name="filter">Optional filter expression applied to the reduced results.</param>
        /// <param name="orderBy">Optional ordering expression.</param>
        /// <param name="descending">Whether to order descending. Default is false.</param>
        /// <param name="skip">Number of results to skip.</param>
        /// <param name="take">Maximum number of results to return.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<IReadOnlyList<TResult>> QueryMapReduceAsync<TResult>(
            string indexName,
            Expression<Func<TResult, bool>>? filter = null,
            Expression<Func<TResult, object>>? orderBy = null,
            bool descending = false,
            int? skip = null,
            int? take = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            using var session = _documentStore.OpenAsyncSession();
            var query = session.Query<TResult>(indexName);

            if (filter != null)
            {
                query = query.Where(filter);
            }

            IQueryable<TResult> sorted = query;

            if (orderBy != null)
            {
                sorted = descending
                    ? query.OrderByDescending(orderBy)
                    : query.OrderBy(orderBy);
            }

            if (skip.HasValue)
            {
                sorted = sorted.Skip(skip.Value);
            }

            if (take.HasValue)
            {
                sorted = sorted.Take(take.Value);
            }

            return await ((IRavenQueryable<TResult>)sorted).ToListAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Queries a Map/Reduce index and returns the first matching result, or null if none found.
        /// </summary>
        public async Task<TResult?> QueryMapReduceFirstAsync<TResult>(
            string indexName,
            Expression<Func<TResult, bool>>? filter = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            using var session = _documentStore.OpenAsyncSession();
            var query = session.Query<TResult>(indexName);

            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Counts results in a Map/Reduce index with an optional filter.
        /// </summary>
        public async Task<int> CountMapReduceAsync<TResult>(
            string indexName,
            Expression<Func<TResult, bool>>? filter = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            using var session = _documentStore.OpenAsyncSession();
            var query = session.Query<TResult>(indexName);

            if (filter != null)
            {
                return await query.CountAsync(filter, ct).ConfigureAwait(false);
            }

            return await query.CountAsync(ct).ConfigureAwait(false);
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
