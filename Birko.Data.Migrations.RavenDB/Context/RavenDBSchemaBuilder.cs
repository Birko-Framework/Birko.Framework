using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Patterns.Schema;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.Indexes;

namespace Birko.Data.Migrations.RavenDB.Context
{
    public class RavenDBSchemaBuilder : ISchemaBuilder
    {
        // TASK-627: the chains handed out, so the runner can fail a migration that never called Build().
        private readonly SchemaBuildLedger _ledger = new();

        /// <inheritdoc />
        public System.Collections.Generic.IReadOnlyList<string> PendingBuilds => _ledger.Pending;

        private readonly IDocumentStore _store;

        public RavenDBSchemaBuilder(IDocumentStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public ICollectionBuilder CreateCollection(string name)
        {
            // RavenDB is document-based — collections are created implicitly on first document insert.
            return _ledger.Track(new RavenCollectionBuilder(name, _store), name);
        }

        public void DropCollection(string name)
        {
            // Delete all documents in the collection
            var operation = _store.Operations.Send(new DeleteByQueryOperation(
                new Raven.Client.Documents.Queries.IndexQuery
                {
                    Query = $"FROM {RavenDBDataMigrator.RqlCollection(name)}"
                }));
            operation.WaitForCompletion();
        }

        public bool CollectionExists(string name)
        {
            // Actually check whether the named collection holds any documents (CR-H064: this used to
            // ignore `name` and unconditionally return true, breaking any "create only if missing"
            // caller). Collection statistics report per-collection document counts.
            var stats = _store.Maintenance.Send(
                new Raven.Client.Documents.Operations.GetCollectionStatisticsOperation());
            return stats.Collections.TryGetValue(name, out var count) && count > 0;
        }

        public IIndexBuilder CreateIndex(string collectionName, string indexName)
        {
            return _ledger.Track(new RavenIndexBuilder(collectionName, indexName, _store), collectionName, indexName);
        }

        public void DropIndex(string collectionName, string indexName)
        {
            _store.Maintenance.Send(new DeleteIndexOperation(indexName));
        }

        public void AddField(string collectionName, FieldDescriptor field)
        {
            // RavenDB is schema-less — no-op
        }

        /// <summary>
        /// Removes the field from every document of the collection with a patch-by-query, waited for like
        /// <see cref="RenameField"/>; the names are held to TASK-640's rules.
        /// </summary>
        /// <remarks>
        /// TASK-655: this was a no-op, so a migration that dropped a field was recorded as applied while every document
        /// kept it (measured).
        /// </remarks>
        public void DropField(string collectionName, string fieldName)
        {
            var collection = RavenDBDataMigrator.RqlCollection(collectionName);
            var field = RavenDBDataMigrator.RqlField(fieldName);
            var operation = _store.Operations.Send(new PatchByQueryOperation(
                new Raven.Client.Documents.Queries.IndexQuery
                {
                    Query = $"FROM {collection} UPDATE {{ delete this.{field}; }}"
                }));
            operation.WaitForCompletion();
        }

        public void RenameField(string collectionName, string oldName, string newName)
        {
            // TASK-640: the names are held to the filter's identifier rule and the collection is escaped, as in the migrator.
            var collection = RavenDBDataMigrator.RqlCollection(collectionName);
            var from = RavenDBDataMigrator.RqlField(oldName);
            var to = RavenDBDataMigrator.RqlField(newName);
            var operation = _store.Operations.Send(new PatchByQueryOperation(
                new Raven.Client.Documents.Queries.IndexQuery
                {
                    Query = $"FROM {collection} UPDATE {{ this.{to} = this.{from}; delete this.{from}; }}"
                }));
            operation.WaitForCompletion();
        }

        private class RavenCollectionBuilder : ICollectionBuilder
        {
            private readonly string _name;
            private readonly IDocumentStore _store;

            public RavenCollectionBuilder(string name, IDocumentStore store)
            {
                _name = name;
                _store = store;
            }

            public ICollectionBuilder WithField(string name, FieldType type,
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

        private class RavenIndexBuilder : IIndexBuilder
        {
            private readonly string _collectionName;
            private readonly string _indexName;
            private readonly IDocumentStore _store;
            private readonly List<(string Name, bool Descending, IndexFieldType FieldType)> _fields = new();
            private bool _unique;

            public RavenIndexBuilder(string collectionName, string indexName, IDocumentStore store)
            {
                _collectionName = collectionName;
                _indexName = indexName;
                _store = store;
            }

            public IIndexBuilder WithField(string name, bool descending = false, IndexFieldType fieldType = IndexFieldType.Standard)
            {
                _fields.Add((name, descending, fieldType));
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
                    "RavenDB", "a sparse index", "a Raven index is a map/reduce definition rather than a field list, so this builder cannot express one", "Use Raw(), or IIndexManager.CreateAsync with a Map/Maps/Reduce entry in IndexDefinition.Properties, which RavenDBIndexManager does honour.");

            /// <inheritdoc cref="Sparse"/>
            public IIndexBuilder WithProperty(string key, object value)
                => throw Birko.Data.Patterns.Schema.IndexBuilderSupport.Unsupported(
                    "RavenDB", $"index property '{key}'", "a Raven index is a map/reduce definition rather than a field list, so this builder cannot express one", "Use Raw(), or IIndexManager.CreateAsync with a Map/Maps/Reduce entry in IndexDefinition.Properties, which RavenDBIndexManager does honour.");

            /// <summary>
            /// Creates the index through <see cref="Birko.Data.RavenDB.IndexManagement.RavenDBIndexManager"/>, the other
            /// door onto the same feature (SH-M200, TASK-152): a map over the declared fields of the collection. TASK-274
            /// refused the whole declaration because the builder created nothing; the manager generates the map and
            /// refuses what a RavenDB index cannot be (Unique, a field name that is not an identifier).
            /// </summary>
            public void Build()
            {
                if (_fields.Count == 0)
                    throw new InvalidOperationException($"Index '{_indexName}' on '{_collectionName}' declares no field.");
                if (_fields.Exists(f => f.Descending))
                    throw Birko.Data.Patterns.Schema.IndexBuilderSupport.Unsupported(
                        "RavenDB", "a descending field", "a RavenDB index field has no direction; ordering is chosen by the query", "Declare the field ascending.");

                var definition = new Birko.Data.Patterns.IndexManagement.IndexDefinition
                {
                    Name = _indexName,
                    Unique = _unique,
                    Fields = _fields.ConvertAll(f => new Birko.Data.Patterns.IndexManagement.IndexField { Name = f.Name, FieldType = f.FieldType }),
                };
                new Birko.Data.RavenDB.IndexManagement.RavenDBIndexManager(_store)
                    .CreateAsync(definition, _collectionName).GetAwaiter().GetResult();
            }
        }
    }
}
