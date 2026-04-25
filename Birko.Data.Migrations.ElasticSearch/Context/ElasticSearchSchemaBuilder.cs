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
        private readonly ElasticClient _client;

        public ElasticSearchSchemaBuilder(ElasticClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ICollectionBuilder CreateCollection(string name)
        {
            if (!_client.Indices.Exists(name).Exists)
            {
                _client.Indices.Create(name, c => c
                    .Settings(s => s
                        .NumberOfShards(1)
                        .NumberOfReplicas(0)
                    )
                );
            }

            return new ElasticCollectionBuilder(name, _client);
        }

        public void DropCollection(string name)
        {
            if (_client.Indices.Exists(name).Exists)
            {
                _client.Indices.Delete(name);
            }
        }

        public bool CollectionExists(string name)
        {
            return _client.Indices.Exists(name).Exists;
        }

        public IIndexBuilder CreateIndex(string collectionName, string indexName)
        {
            return new ElasticIndexBuilder(collectionName, indexName, _client);
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
            // Use _update_by_query to rename fields across all documents
            _client.UpdateByQuery<dynamic>(descriptor => descriptor
                .Index(collectionName)
                .Query(q => q.MatchAll())
                .Script(s => s
                    .Source($"ctx._source.{newName} = ctx._source.remove('{oldName}')")
                    .Lang("painless")
                )
            );
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

            public IIndexBuilder Sparse() => this;

            public IIndexBuilder WithProperty(string key, object value) => this;

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
