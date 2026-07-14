using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Birko.Data.Migrations.Context;
using Nest;

namespace Birko.Data.Migrations.ElasticSearch.Context
{
    public class ElasticSearchDataMigrator : IDataMigrator
    {
        private readonly ElasticClient _client;

        public ElasticSearchDataMigrator(ElasticClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates)
        {
            if (updates == null || updates.Count == 0) return;

            var painlessSet = BuildPainlessSource(updates, out var scriptParams);

            var response = _client.UpdateByQuery<dynamic>(descriptor =>
            {
                descriptor.Index(collection);

                if (!string.IsNullOrWhiteSpace(filterJson) && filterJson.Trim() != "{}")
                {
                    descriptor.Query(q => ParseFilter(q, filterJson));
                }
                else
                {
                    descriptor.Query(q => q.MatchAll());
                }

                descriptor.Script(s => s
                    .Source(painlessSet)
                    .Lang("painless")
                    .Params(scriptParams)
                );

                return descriptor;
            });

            EnsureValid(response, $"UpdateByQuery on index '{collection}'");
        }

        public void DeleteDocuments(string collection, string filterJson)
        {
            var response = _client.DeleteByQuery<dynamic>(descriptor =>
            {
                descriptor.Index(collection);

                if (!string.IsNullOrWhiteSpace(filterJson) && filterJson.Trim() != "{}")
                {
                    descriptor.Query(q => ParseFilter(q, filterJson));
                }
                else
                {
                    descriptor.Query(q => q.MatchAll());
                }

                return descriptor;
            });

            EnsureValid(response, $"DeleteByQuery on index '{collection}'");
        }

        public long CountDocuments(string collection, string? filterJson = null)
        {
            var response = _client.Count<dynamic>(descriptor =>
            {
                descriptor.Index(collection);

                if (!string.IsNullOrWhiteSpace(filterJson) && filterJson.Trim() != "{}")
                {
                    descriptor.Query(q => ParseFilter(q, filterJson));
                }

                return descriptor;
            });

            return response.Count;
        }

        public void CopyData(string sourceCollection, string targetCollection, string? transformJson = null)
        {
            // CR-L144: the ElasticSearch reindex path does not apply a document transform. Rather than
            // silently producing untransformed data, reject a transform request so the caller learns it
            // is unimplemented for this backend.
            if (!string.IsNullOrWhiteSpace(transformJson))
            {
                throw new NotSupportedException(
                    "ElasticSearchDataMigrator.CopyData does not support transformJson; the server-side " +
                    "reindex copies documents unchanged. Use a separate update step for transformations.");
            }

            var reindexResponse = _client.ReindexOnServer(r => r
                .Source(s => s.Index(sourceCollection))
                .Destination(d => d.Index(targetCollection))
                .WaitForCompletion(true)
            );
            EnsureValid(reindexResponse, $"ReindexOnServer '{sourceCollection}' -> '{targetCollection}'");

            EnsureValid(_client.Indices.Refresh(targetCollection), $"Refresh index '{targetCollection}'");
        }

        public void BulkInsert(string collection, IEnumerable<IDictionary<string, object>> documents)
        {
            if (documents == null) return;

            var bulkDescriptor = new BulkDescriptor();
            var hasDocuments = false;

            foreach (var doc in documents)
            {
                if (doc == null || doc.Count == 0) continue;

                hasDocuments = true;
                bulkDescriptor.Index<object>(idx => idx
                    .Index(collection)
                    .Document(doc)
                );
            }

            if (hasDocuments)
            {
                var bulkResponse = _client.Bulk(bulkDescriptor);
                EnsureValid(bulkResponse, $"Bulk insert into '{collection}'");
                if (bulkResponse.Errors)
                {
                    var firstError = bulkResponse.ItemsWithErrors.FirstOrDefault()?.Error?.Reason;
                    throw new InvalidOperationException(
                        $"Bulk insert into '{collection}' had item-level errors: {firstError}. {bulkResponse.DebugInformation}");
                }

                EnsureValid(_client.Indices.Refresh(collection), $"Refresh index '{collection}'");
            }
        }

        /// <summary>
        /// Throws when an ElasticSearch migration response is invalid — otherwise a failed data step
        /// (mapping/version conflict, reindex error, partial bulk failure) would be silently swallowed
        /// and the migration recorded as applied (CR-H059). Mirrors ElasticSearchMigrationStore.
        /// </summary>
        private static void EnsureValid(IResponse response, string operation)
        {
            if (response == null || !response.IsValid)
            {
                throw new InvalidOperationException(
                    $"ElasticSearch migration step failed: {operation}. {response?.DebugInformation}",
                    response?.OriginalException);
            }
        }

        private static QueryContainer ParseFilter(QueryContainerDescriptor<dynamic> q, string filterJson)
        {
            using var doc = JsonDocument.Parse(filterJson);
            var mustClauses = new List<QueryContainer>();

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var fieldName = property.Name;

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var op in property.Value.EnumerateObject())
                    {
                        var value = ExtractValue(op.Value);
                        switch (op.Name)
                        {
                            case "$gt":
                                mustClauses.Add(new QueryContainer(new NumericRangeQuery
                                {
                                    Field = fieldName,
                                    GreaterThan = ToRangeBound(value, fieldName, op.Name)
                                }));
                                break;
                            case "$gte":
                                mustClauses.Add(new QueryContainer(new NumericRangeQuery
                                {
                                    Field = fieldName,
                                    GreaterThanOrEqualTo = ToRangeBound(value, fieldName, op.Name)
                                }));
                                break;
                            case "$lt":
                                mustClauses.Add(new QueryContainer(new NumericRangeQuery
                                {
                                    Field = fieldName,
                                    LessThan = ToRangeBound(value, fieldName, op.Name)
                                }));
                                break;
                            case "$lte":
                                mustClauses.Add(new QueryContainer(new NumericRangeQuery
                                {
                                    Field = fieldName,
                                    LessThanOrEqualTo = ToRangeBound(value, fieldName, op.Name)
                                }));
                                break;
                            case "$ne":
                                mustClauses.Add(new QueryContainer(new BoolQuery
                                {
                                    MustNot = new[] { new QueryContainer(new TermQuery { Field = fieldName, Value = value }) }
                                }));
                                break;
                            default:
                                mustClauses.Add(new QueryContainer(new TermQuery
                                {
                                    Field = fieldName,
                                    Value = value
                                }));
                                break;
                        }
                    }
                }
                else
                {
                    mustClauses.Add(new QueryContainer(new TermQuery
                    {
                        Field = fieldName,
                        Value = ExtractValue(property.Value)
                    }));
                }
            }

            return new QueryContainer(new BoolQuery { Must = mustClauses });
        }

        /// <summary>
        /// Builds a Painless update script that assigns each value from a script param
        /// (<c>ctx._source.field = params.pN</c>) rather than interpolating the value into the source.
        /// Hand-formatting broke on quotes/backslashes and produced invalid literals for bool /
        /// DateTime / decimal — letting Nest serialize the params fixes it (CR-H058).
        /// </summary>
        internal static string BuildPainlessSource(IDictionary<string, object> updates, out Dictionary<string, object> scriptParams)
        {
            scriptParams = new Dictionary<string, object>();
            var keys = updates.Keys.ToList();
            var sourceParts = new List<string>(keys.Count);
            for (var i = 0; i < keys.Count; i++)
            {
                var paramName = $"p{i}";
                scriptParams[paramName] = updates[keys[i]];
                sourceParts.Add($"ctx._source.{keys[i]} = params.{paramName}");
            }
            return string.Join("; ", sourceParts);
        }

        private static object? ExtractValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        /// <summary>
        /// Coerces a range-operator value to a double for a NumericRangeQuery, throwing a clear
        /// <see cref="ArgumentException"/> on a null/non-numeric value (CR-L143). Convert.ToDouble(null)
        /// silently returns 0, so a malformed filter like {"x":{"$gt":null}} used to become a range &gt; 0.
        /// </summary>
        internal static double ToRangeBound(object? value, string field, string op)
        {
            if (value == null)
            {
                throw new ArgumentException($"Range operator '{op}' on field '{field}' requires a non-null numeric value.");
            }
            try
            {
                return Convert.ToDouble(value);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                throw new ArgumentException(
                    $"Range operator '{op}' on field '{field}' requires a numeric value, got '{value}'.", ex);
            }
        }
    }
}
