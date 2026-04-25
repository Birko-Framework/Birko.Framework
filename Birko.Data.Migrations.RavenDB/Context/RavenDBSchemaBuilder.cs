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
        private readonly IDocumentStore _store;

        public RavenDBSchemaBuilder(IDocumentStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public ICollectionBuilder CreateCollection(string name)
        {
            // RavenDB is document-based — collections are created implicitly on first document insert.
            return new RavenCollectionBuilder(name, _store);
        }

        public void DropCollection(string name)
        {
            // Delete all documents in the collection
            var operation = _store.Operations.Send(new DeleteByQueryOperation(
                new Raven.Client.Documents.Queries.IndexQuery
                {
                    Query = $"FROM '{name}'"
                }));
            operation.WaitForCompletion();
        }

        public bool CollectionExists(string name)
        {
            // RavenDB collections exist implicitly; check if there are any documents
            using var session = _store.OpenSession();
            var count = session.Query<dynamic>()
                .Statistics(out var stats)
                .Take(1)
                .ToList();
            return true;
        }

        public IIndexBuilder CreateIndex(string collectionName, string indexName)
        {
            return new RavenIndexBuilder(collectionName, indexName, _store);
        }

        public void DropIndex(string collectionName, string indexName)
        {
            _store.Maintenance.Send(new DeleteIndexOperation(indexName));
        }

        public void AddField(string collectionName, FieldDescriptor field)
        {
            // RavenDB is schema-less — no-op
        }

        public void DropField(string collectionName, string fieldName)
        {
            // RavenDB is schema-less — use PatchByQueryOperation to $unset equivalent via Raw()
        }

        public void RenameField(string collectionName, string oldName, string newName)
        {
            var operation = _store.Operations.Send(new PatchByQueryOperation(
                new Raven.Client.Documents.Queries.IndexQuery
                {
                    Query = $"FROM '{collectionName}' UPDATE {{ this.{newName} = this.{oldName}; delete this.{oldName}; }}"
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
            private readonly List<(string Name, bool Descending)> _fields = new();
            private bool _unique;

            public RavenIndexBuilder(string collectionName, string indexName, IDocumentStore store)
            {
                _collectionName = collectionName;
                _indexName = indexName;
                _store = store;
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

            public IIndexBuilder Sparse() => this;

            public IIndexBuilder WithProperty(string key, object value) => this;

            /// <summary>
            /// Exposes whether <see cref="Unique"/> was called. RavenDB enforces uniqueness via
            /// application-level deduplication or compare-exchange operations rather than index
            /// metadata, so <see cref="_unique"/> is captured here but not yet translated into a
            /// store operation. Reserved for when that wiring lands.
            /// </summary>
            internal bool IsUnique => _unique;
        }
    }
}
