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

            var painlessSet = string.Join("; ", updates.Select(kvp =>
            {
                var valueStr = kvp.Value is string s ? $"'{s}'" : kvp.Value?.ToString() ?? "null";
                return $"ctx._source.{kvp.Key} = {valueStr}";
            }));

            _client.UpdateByQuery<dynamic>(descriptor =>
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
                );

                return descriptor;
            });
        }

        public void DeleteDocuments(string collection, string filterJson)
        {
            _client.DeleteByQuery<dynamic>(descriptor =>
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
            _client.ReindexOnServer(r => r
                .Source(s => s.Index(sourceCollection))
                .Destination(d => d.Index(targetCollection))
                .WaitForCompletion(true)
            );

            _client.Indices.Refresh(targetCollection);
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
                _client.Bulk(bulkDescriptor);
                _client.Indices.Refresh(collection);
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
                                    GreaterThan = Convert.ToDouble(value)
                                }));
                                break;
                            case "$gte":
                                mustClauses.Add(new QueryContainer(new NumericRangeQuery
                                {
                                    Field = fieldName,
                                    GreaterThanOrEqualTo = Convert.ToDouble(value)
                                }));
                                break;
                            case "$lt":
                                mustClauses.Add(new QueryContainer(new NumericRangeQuery
                                {
                                    Field = fieldName,
                                    LessThan = Convert.ToDouble(value)
                                }));
                                break;
                            case "$lte":
                                mustClauses.Add(new QueryContainer(new NumericRangeQuery
                                {
                                    Field = fieldName,
                                    LessThanOrEqualTo = Convert.ToDouble(value)
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
    }
}
