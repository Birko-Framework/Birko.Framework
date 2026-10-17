using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Patterns.Schema;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;

namespace Birko.Data.Migrations.InfluxDB.Context
{
    public class InfluxDBSchemaBuilder : ISchemaBuilder
    {
        // TASK-627: the chains handed out, so the runner can fail a migration that never called Build().
        private readonly SchemaBuildLedger _ledger = new();

        /// <inheritdoc />
        public System.Collections.Generic.IReadOnlyList<string> PendingBuilds => _ledger.Pending;

        private readonly InfluxDBClient _client;
        private readonly string _organization;

        public InfluxDBSchemaBuilder(InfluxDBClient client, string organization)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _organization = organization ?? throw new ArgumentNullException(nameof(organization));
        }

        public ICollectionBuilder CreateCollection(string name)
        {
            var existing = InfluxBucketLookup.Find(_client, _organization, name);
            if (existing == null)
            {
                InfluxBucketLookup.Create(_client, _organization, name, null);
            }

            return _ledger.Track(new InfluxCollectionBuilder(name, _client, _organization), name);
        }

        public void DropCollection(string name)
        {
            var bucket = InfluxBucketLookup.Find(_client, _organization, name);

            if (bucket != null)
            {
                _client.GetBucketsApi().DeleteBucketAsync(bucket).GetAwaiter().GetResult();
            }
        }

        public bool CollectionExists(string name)
        {
            return InfluxBucketLookup.Find(_client, _organization, name) != null;
        }

        public IIndexBuilder CreateIndex(string collectionName, string indexName)
        {
            // InfluxDB does not support custom indexes — time-series structure is implicit.
            return _ledger.Track(new InfluxIndexBuilder(collectionName, indexName), collectionName, indexName);
        }

        public void DropIndex(string collectionName, string indexName)
        {
            // InfluxDB does not support custom indexes — no-op.
        }

        public void AddField(string collectionName, FieldDescriptor field)
        {
            // InfluxDB is schema-less — fields and tags are defined at write time. No-op.
        }

        // TASK-642 (rule 22): both were silent no-ops, so the runner recorded a migration that changed nothing as applied
        // (measured live). InfluxDB can do neither, so each refuses, as UpdateDocuments does.
        public void DropField(string collectionName, string fieldName)
            => throw new NotSupportedException(
                $"InfluxDB cannot drop field '{fieldName}' from '{collectionName}': a delete predicate cannot name a field, so points keep it. Rewrite the series without it, or use Raw().");

        public void RenameField(string collectionName, string oldName, string newName)
            => throw new NotSupportedException(
                $"InfluxDB cannot rename field '{oldName}' to '{newName}' in '{collectionName}': points are immutable. Rewrite the series under the new name, or use Raw().");

        private class InfluxCollectionBuilder : ICollectionBuilder
        {
            private readonly string _name;
            private readonly InfluxDBClient _client;
            private readonly string _organization;

            public InfluxCollectionBuilder(string name, InfluxDBClient client, string organization)
            {
                _name = name;
                _client = client;
                _organization = organization;
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

        private class InfluxIndexBuilder : IIndexBuilder
        {
            private readonly string _collectionName;
            private readonly string _indexName;

            public InfluxIndexBuilder(string collectionName, string indexName)
            {
                _collectionName = collectionName;
                _indexName = indexName;
            }

            public IIndexBuilder WithField(string name, bool descending = false, IndexFieldType fieldType = IndexFieldType.Standard)
            {
                return this;
            }

            public IIndexBuilder Unique() => this;

            public IIndexBuilder Sparse() => this;

            public IIndexBuilder WithProperty(string key, object value) => this;

            /// <summary>
            /// <b>Refuses (TASK-274).</b> InfluxDB has no custom indexes at all — the time-series structure
            /// is implicit — so a declared index here can never be honoured. The chain's other methods stay
            /// no-ops deliberately: refusing at the terminal reports the whole declaration once, rather than
            /// failing on whichever knob the author happened to touch first.
            /// </summary>
            public void Build()
                => throw Birko.Data.Patterns.Schema.IndexBuilderSupport.Unsupported(
                    "InfluxDB",
                    $"index '{_indexName}'",
                    "InfluxDB has no custom indexes — the time-series structure is implicit",
                    "Remove the declaration; tag and time semantics come from the measurement schema.");
        }
    }
}
