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
        private readonly InfluxDBClient _client;
        private readonly string _organization;

        public InfluxDBSchemaBuilder(InfluxDBClient client, string organization)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _organization = organization ?? throw new ArgumentNullException(nameof(organization));
        }

        public ICollectionBuilder CreateCollection(string name)
        {
            var bucketsApi = _client.GetBucketsApi();

            var existing = FindBucket(bucketsApi, name);
            if (existing == null)
            {
                bucketsApi.CreateBucketAsync(name, (BucketRetentionRules?)null, _organization)
                    .GetAwaiter().GetResult();
            }

            return new InfluxCollectionBuilder(name, _client, _organization);
        }

        public void DropCollection(string name)
        {
            var bucketsApi = _client.GetBucketsApi();
            var bucket = FindBucket(bucketsApi, name);

            if (bucket != null)
            {
                bucketsApi.DeleteBucketAsync(bucket).GetAwaiter().GetResult();
            }
        }

        public bool CollectionExists(string name)
        {
            var bucketsApi = _client.GetBucketsApi();
            return FindBucket(bucketsApi, name) != null;
        }

        public IIndexBuilder CreateIndex(string collectionName, string indexName)
        {
            // InfluxDB does not support custom indexes — time-series structure is implicit.
            return new InfluxIndexBuilder(collectionName, indexName);
        }

        public void DropIndex(string collectionName, string indexName)
        {
            // InfluxDB does not support custom indexes — no-op.
        }

        public void AddField(string collectionName, FieldDescriptor field)
        {
            // InfluxDB is schema-less — fields and tags are defined at write time. No-op.
        }

        public void DropField(string collectionName, string fieldName)
        {
            // InfluxDB does not support dropping fields. Use delete with predicate via Raw() if needed.
        }

        public void RenameField(string collectionName, string oldName, string newName)
        {
            // InfluxDB does not support field renaming. Data must be rewritten.
        }

        private static Bucket? FindBucket(BucketsApi bucketsApi, string bucketNameOrId)
        {
            var buckets = bucketsApi.FindBucketsAsync().GetAwaiter().GetResult();
            return buckets.FirstOrDefault(b =>
                b.Name.Equals(bucketNameOrId, StringComparison.OrdinalIgnoreCase) ||
                (b.Id != null && b.Id.Equals(bucketNameOrId, StringComparison.OrdinalIgnoreCase)));
        }

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
        }
    }
}
