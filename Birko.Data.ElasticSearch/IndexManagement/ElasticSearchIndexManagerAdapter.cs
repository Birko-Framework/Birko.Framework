using Birko.Data.Patterns.IndexManagement;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.ElasticSearch.IndexManagement
{
    /// <summary>
    /// Adapts the existing <see cref="IndexManager"/> to the uniform <see cref="IIndexManager"/> interface.
    /// In Elasticsearch, "indexes" are containers (not secondary indexes):
    /// <list type="bullet">
    ///   <item><c>scope</c> is ignored.</item>
    ///   <item><c>indexName</c> is the ES index name.</item>
    ///   <item><see cref="IndexDefinition.Fields"/> are mapped to ES field mappings (auto-map).</item>
    /// </list>
    /// For full ES-specific capabilities (aliases, templates, reindex), use <see cref="IndexManager"/> directly.
    /// </summary>
    public class ElasticSearchIndexManagerAdapter : IIndexManager
    {
        private readonly IndexManager _indexManager;
        private readonly ElasticClient _client;

        public ElasticSearchIndexManagerAdapter(ElasticClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _indexManager = new IndexManager(client);
        }

        public ElasticSearchIndexManagerAdapter(ElasticClient client, IndexManager indexManager)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _indexManager = indexManager ?? throw new ArgumentNullException(nameof(indexManager));
        }

        /// <summary>
        /// Gets the underlying ES-specific <see cref="IndexManager"/> for full capabilities.
        /// </summary>
        public IndexManager Native => _indexManager;

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string indexName, string? scope = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));
            return await _indexManager.IndexExistsAsync(indexName, ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task CreateAsync(IndexDefinition definition, string? scope = null, CancellationToken ct = default)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Name)) throw new ArgumentException("Index name is required.", nameof(definition));

            Func<CreateIndexDescriptor, ICreateIndexRequest>? descriptor = null;

            // SH-M238 (TASK-154): every flag this lane cannot honour used to be dropped, so the call that builds an
            // enforced unique index on SQL built an unconstrained index here and reported success (rule 55). An
            // ElasticSearch index is a container, not a constraint: Unique, Sparse and ExpireAfter have no meaning
            // on it. Fields need none: every field of a document is indexed by its mapping.
            if (definition.Unique)
                throw new NotSupportedException($"ElasticSearch has no unique index; '{definition.Name}' cannot be created Unique.");
            if (definition.Sparse)
                throw new NotSupportedException($"ElasticSearch has no sparse index; '{definition.Name}' cannot be created Sparse.");
            definition.RefusePredicates("ElasticSearch"); // TASK-636
            if (definition.ExpireAfter.HasValue)
                throw new NotSupportedException($"ElasticSearch has no TTL index; '{definition.Name}' cannot expire documents. Delete them on a schedule.");

            int? shards = null, replicas = null;
            if (definition.Properties != null)
            {
                foreach (var (key, value) in definition.Properties)
                {
                    var number = key is "NumberOfShards" or "NumberOfReplicas" ? AsInt(key, value) : (int?)null;
                    if (key == "NumberOfShards") shards = number;
                    else if (key == "NumberOfReplicas") replicas = number;
                    else throw new NotSupportedException(
                        $"Unknown ElasticSearch index property '{key}' on '{definition.Name}'; supported: NumberOfShards, NumberOfReplicas.");
                }
            }

            if (shards.HasValue || replicas.HasValue)
            {
                descriptor = c => c.Settings(s =>
                {
                    if (shards.HasValue) s = s.NumberOfShards(shards.Value);
                    if (replicas.HasValue) s = s.NumberOfReplicas(replicas.Value);
                    return s;
                });
            }

            try
            {
                await _indexManager.CreateIndexAsync(definition.Name, descriptor, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not IndexManagementException)
            {
                throw new IndexManagementException(
                    $"Failed to create ES index '{definition.Name}'.",
                    definition.Name, scope, ex);
            }
        }

        /// <inheritdoc />
        public async Task DropAsync(string indexName, string? scope = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            try
            {
                await _indexManager.DeleteIndexAsync(indexName, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not IndexManagementException)
            {
                throw new IndexManagementException(
                    $"Failed to delete ES index '{indexName}'.",
                    indexName, scope, ex);
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Patterns.IndexManagement.IndexInfo>> ListAsync(string? scope = null, CancellationToken ct = default)
        {
            var catResponse = await _client.Cat.IndicesAsync(d => d, ct).ConfigureAwait(false);

            // SH-M236 (TASK-154): a failed _cat request read as a cluster with no indexes. The SQL managers let the
            // failure propagate, and so does this one now.
            if (!catResponse.IsValid)
                throw new IndexManagementException(
                    $"Failed to list ES indexes: {catResponse.DebugInformation}", "*", scope, catResponse.OriginalException);

            return catResponse.Records
                .Where(r => !r.Index.StartsWith(".")) // skip system indexes
                .Select(r => new Patterns.IndexManagement.IndexInfo
                {
                    Name = r.Index,
                    SizeInBytes = ParseSizeToBytes(r.StoreSize),
                    State = r.Status ?? "unknown",
                    Properties = new Dictionary<string, object>
                    {
                        ["DocsCount"] = r.DocsCount ?? "0",
                        ["Health"] = r.Health ?? "unknown",
                        ["PrimaryShards"] = r.Primary ?? "0",
                        ["Replicas"] = r.Replica ?? "0"
                    }
                })
                .ToList();
        }

        /// <inheritdoc />
        public async Task<Patterns.IndexManagement.IndexInfo?> GetInfoAsync(string indexName, string? scope = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            // SH-M237 (TASK-154): every failure (a cluster error, a bad request, an auth failure) answered null, "no such
            // index". Only that answer is null now, asked as a question rather than read off an exception.
            if (!await _indexManager.IndexExistsAsync(indexName, ct).ConfigureAwait(false))
                return null;

            {
                var esInfo = await _indexManager.GetIndexInfoAsync(indexName, ct).ConfigureAwait(false);

                return new Patterns.IndexManagement.IndexInfo
                {
                    Name = esInfo.Name,
                    SizeInBytes = esInfo.SizeInBytes,
                    State = esInfo.State,
                    Properties = new Dictionary<string, object>
                    {
                        ["DocumentCount"] = esInfo.DocumentCount,
                        ["NumberOfShards"] = esInfo.NumberOfShards,
                        ["NumberOfReplicas"] = esInfo.NumberOfReplicas,
                        ["Health"] = esInfo.Health,
                        ["Aliases"] = esInfo.Aliases
                    }
                };
            }
        }

        private static int AsInt(string key, object? value) => value switch
        {
            int i => i,
            long l when l is >= 0 and <= int.MaxValue => (int)l,
            string text when int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw new NotSupportedException($"ElasticSearch index property '{key}' must be a whole number, not '{value}'."),
        };

        #region Helpers

        private static long ParseSizeToBytes(string? sizeStr)
        {
            if (string.IsNullOrWhiteSpace(sizeStr)) return -1;

            // ES _cat returns sizes like "1.2kb", "3.4mb", "5.6gb"
            sizeStr = sizeStr.Trim().ToLowerInvariant();

            double multiplier = 1;
            string numPart = sizeStr;

            if (sizeStr.EndsWith("gb"))
            {
                multiplier = 1024 * 1024 * 1024;
                numPart = sizeStr[..^2];
            }
            else if (sizeStr.EndsWith("mb"))
            {
                multiplier = 1024 * 1024;
                numPart = sizeStr[..^2];
            }
            else if (sizeStr.EndsWith("kb"))
            {
                multiplier = 1024;
                numPart = sizeStr[..^2];
            }
            else if (sizeStr.EndsWith("b"))
            {
                numPart = sizeStr[..^1];
            }

            if (double.TryParse(numPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                return (long)(val * multiplier);
            }

            return -1;
        }

        #endregion
    }
}
