using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Patterns.Schema;
using Nest;

namespace Birko.Data.Migrations.ElasticSearch.Context
{
    public class ElasticSearchSchemaBuilder : ISchemaBuilder
    {
        // TASK-627: the chains handed out, so the runner can fail a migration that never called Build().
        private readonly SchemaBuildLedger _ledger = new();

        /// <inheritdoc />
        public System.Collections.Generic.IReadOnlyList<string> PendingBuilds => _ledger.Pending;

        private readonly ElasticClient _client;

        public ElasticSearchSchemaBuilder(ElasticClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ICollectionBuilder CreateCollection(string name)
        {
            if (!IndexExists(name))
            {
                ElasticSearchDataMigrator.EnsureValid(_client.Indices.Create(name, c => c
                    .Settings(s => s
                        .NumberOfShards(1)
                        .NumberOfReplicas(0)
                    )
                ), $"Create index '{name}'");
            }

            return _ledger.Track(new ElasticCollectionBuilder(name, _client), name);
        }

        public void DropCollection(string name)
        {
            if (IndexExists(name))
            {
                ElasticSearchDataMigrator.EnsureValid(_client.Indices.Delete(name), $"Delete index '{name}'");
            }
        }

        public bool CollectionExists(string name) => IndexExists(name);

        /// <summary>
        /// Whether the index exists; a check that got no answer throws.
        /// </summary>
        /// <remarks>
        /// TASK-643, the SH-M202 rule (TASK-152) the migration store already keeps. NEST's <c>Exists</c> is "status 200", so an
        /// unreachable or unauthorized cluster read as "absent": a create was attempted and not checked, a drop did
        /// nothing, and the migration was recorded. Only a 404 means absent.
        /// </remarks>
        private bool IndexExists(string name)
        {
            var exists = _client.Indices.Exists(name);
            if (!exists.IsValid && exists.ApiCall?.HttpStatusCode != 404)
            {
                throw new InvalidOperationException(
                    $"ElasticSearch migration step failed: the existence check for index '{name}' got no answer. {exists.DebugInformation}",
                    exists.OriginalException);
            }
            return exists.Exists;
        }

        public IIndexBuilder CreateIndex(string collectionName, string indexName)
        {
            return _ledger.Track(new ElasticIndexBuilder(collectionName, indexName, _client), collectionName, indexName);
        }

        public void DropIndex(string collectionName, string indexName)
        {
            // ElasticSearch indices are managed differently — use Raw() for template/alias manipulation
        }

        public void AddField(string collectionName, FieldDescriptor field)
        {
            // ElasticSearch mappings are dynamic — new fields are auto-mapped on indexing.
            // This is a no-op since ES does not require explicit field additions.
        }

        public void DropField(string collectionName, string fieldName)
        {
            // ElasticSearch does not support removing a field from an existing mapping.
            // This is a no-op; use reindex with a script via Raw() if needed.
        }

        public void RenameField(string collectionName, string oldName, string newName)
        {
            // SH-M199 (TASK-152): the response was discarded, so a refused script (missing index, mapping conflict, compile
            // error) left every document unrenamed and the migration was recorded; and both names were interpolated into
            // the Painless source. The names are params now, only documents that HAVE the old field are touched (others
            // gained the new one as null), and the response is checked like every data step.
            var response = _client.UpdateByQuery<dynamic>(descriptor => descriptor
                .Index(collectionName)
                .Query(q => q.Exists(e => e.Field(oldName)))
                .Refresh()
                .Script(s => s
                    .Source("ctx._source[params.newName] = ctx._source.remove(params.oldName)")
                    .Lang("painless")
                    .Params(p => p.Add("oldName", oldName).Add("newName", newName))
                )
            );
            ElasticSearchDataMigrator.EnsureValid(response, $"RenameField '{oldName}' -> '{newName}' on index '{collectionName}'");
        }

        private class ElasticCollectionBuilder : ICollectionBuilder
        {
            private readonly string _name;
            private readonly ElasticClient _client;

            public ElasticCollectionBuilder(string name, ElasticClient client)
            {
                _name = name;
                _client = client;
            }

            public ICollectionBuilder WithField(string name, Birko.Data.Patterns.Schema.FieldType type,
                bool isPrimary = false, bool isUnique = false,
                bool isRequired = false, int? maxLength = null,
                int? precision = null, int? scale = null,
                bool isAutoIncrement = false, object? defaultValue = null)
            {
                return this;
            }

            public ICollectionBuilder WithField(FieldDescriptor field)
            {
                return this;
            }
        }

        private class ElasticIndexBuilder : IIndexBuilder
        {
            private readonly string _collectionName;
            private readonly string _indexName;
            private readonly ElasticClient _client;
            private readonly List<(string Name, bool Descending)> _fields = new();
            private bool _unique;

            public ElasticIndexBuilder(string collectionName, string indexName, ElasticClient client)
            {
                _collectionName = collectionName;
                _indexName = indexName;
                _client = client;
            }

            public IIndexBuilder WithField(string name, bool descending = false, IndexFieldType fieldType = IndexFieldType.Standard)
            {
                _fields.Add((name, descending));
                return this;
            }

            public IIndexBuilder Unique()
            {
                _unique = true;
                return this;
            }

            /// <summary>
            /// Refused, with everything else this builder accumulates (TASK-274). See <see cref="Build"/>.
            /// </summary>
            public IIndexBuilder Sparse()
                => throw Birko.Data.Patterns.Schema.IndexBuilderSupport.Unsupported(
                    "ElasticSearch", "a sparse index", "ElasticSearch has no secondary-index concept — its inverted index comes from the mapping", "Use Raw() to put a mapping, template or alias.");

            /// <inheritdoc cref="Sparse"/>
            public IIndexBuilder WithProperty(string key, object value)
                => throw Birko.Data.Patterns.Schema.IndexBuilderSupport.Unsupported(
                    "ElasticSearch", $"index property '{key}'", "ElasticSearch has no secondary-index concept — its inverted index comes from the mapping", "Use Raw() to put a mapping, template or alias.");

            /// <summary>
            /// <b>Refuses the whole declaration (TASK-274).</b> This builder used to inherit
            /// <c>IIndexBuilder.Build()</c>'s no-op default while accumulating fields and a <c>Unique()</c> flag
            /// and holding a live client — so a migration read as though it had declared an index and the
            /// database never got one. That is the lost-flag defect TASK-246 fixed in the SQL builder, total
            /// rather than partial. Silence was the worst of the three options; refusing is honest and costs
            /// nothing, because nothing called it (measured: 0 uses across all 16 consumer repos).
            /// </summary>
            public void Build()
                => throw Birko.Data.Patterns.Schema.IndexBuilderSupport.NotImplementedHere(
                    "ElasticSearch", _indexName, "Use Raw() to put a mapping, template or alias.");

            /// <summary>
            /// Exposes whether <see cref="Unique"/> was called. Elasticsearch has no native
            /// unique-constraint concept — uniqueness is typically enforced at the application
            /// layer or by using document _id as the uniqueness key. <see cref="_unique"/> is
            /// captured here but not yet translated into an index mapping. Reserved for when
            /// that wiring lands (likely as a create-if-not-exists + version constraint pattern).
            /// </summary>
            internal bool IsUnique => _unique;
        }
    }
}
