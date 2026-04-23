using System;
using System.Collections.Generic;
using System.Text.Json;
using Birko.Data.Migrations.Context;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Session;

namespace Birko.Data.Migrations.RavenDB.Context
{
    public class RavenDBDataMigrator : IDataMigrator
    {
        private readonly IDocumentStore _store;

        public RavenDBDataMigrator(IDocumentStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates)
        {
            if (updates == null || updates.Count == 0) return;

            var patchScript = string.Join("; ", updates.Select(kvp =>
            {
                var valueStr = kvp.Value is string s ? $"'{s}'" : kvp.Value?.ToString() ?? "null";
                return $"this.{kvp.Key} = {valueStr}";
            }));

            var query = $"FROM '{collection}'";
            var whereClause = ParseFilterToRql(filterJson);
            if (!string.IsNullOrEmpty(whereClause))
                query += $" WHERE {whereClause}";

            query += $" UPDATE {{ {patchScript}; }}";

            var operation = _store.Operations.Send(new PatchByQueryOperation(
                new Raven.Client.Documents.Queries.IndexQuery { Query = query }));
            operation.WaitForCompletion();
        }

        public void DeleteDocuments(string collection, string filterJson)
        {
            var query = $"FROM '{collection}'";
            var whereClause = ParseFilterToRql(filterJson);
            if (!string.IsNullOrEmpty(whereClause))
                query += $" WHERE {whereClause}";

            var operation = _store.Operations.Send(new DeleteByQueryOperation(
                new Raven.Client.Documents.Queries.IndexQuery { Query = query }));
            operation.WaitForCompletion();
        }

        public long CountDocuments(string collection, string? filterJson = null)
        {
            using var session = _store.OpenSession();
            var query = session.Advanced.DocumentQuery<dynamic>(collection);

            if (!string.IsNullOrWhiteSpace(filterJson) && filterJson.Trim() != "{}")
            {
                ApplyFilterToQuery(query, filterJson);
            }

            var stats = session.Query<dynamic>()
                .Statistics(out var queryStats)
                .Take(0)
                .ToList();

            return queryStats.TotalResults;
        }

        public void CopyData(string sourceCollection, string targetCollection, string? transformJson = null)
        {
            using var session = _store.OpenSession();

            var sourceDocs = session.Query<dynamic>(sourceCollection)
                .Customize(x => x.NoCaching())
                .Take(int.MaxValue)
                .ToList();

            foreach (var doc in sourceDocs)
            {
                session.Store(doc);
            }

            session.SaveChanges();
        }

        public void BulkInsert(string collection, IEnumerable<IDictionary<string, object>> documents)
        {
            if (documents == null) return;

            using var bulkInsert = _store.BulkInsert();
            foreach (var doc in documents)
            {
                if (doc == null || doc.Count == 0) continue;
                bulkInsert.Store(doc);
            }
        }

        private static string ParseFilterToRql(string? filterJson)
        {
            if (string.IsNullOrWhiteSpace(filterJson) || filterJson!.Trim() == "{}")
                return string.Empty;

            using var doc = JsonDocument.Parse(filterJson);
            var conditions = new List<string>();

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var fieldName = property.Name;

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var op in property.Value.EnumerateObject())
                    {
                        var value = ExtractValue(op.Value);
                        var rqlOp = op.Name switch
                        {
                            "$gt" => ">",
                            "$gte" => ">=",
                            "$lt" => "<",
                            "$lte" => "<=",
                            "$ne" => "!=",
                            _ => "="
                        };
                        var valueLiteral = value is string s ? $"'{s}'" : value?.ToString() ?? "null";
                        conditions.Add($"{fieldName} {rqlOp} {valueLiteral}");
                    }
                }
                else
                {
                    var value = ExtractValue(property.Value);
                    var valueLiteral = value is string s2 ? $"'{s2}'" : value?.ToString() ?? "null";
                    conditions.Add($"{fieldName} = {valueLiteral}");
                }
            }

            return string.Join(" AND ", conditions);
        }

        private static void ApplyFilterToQuery(IDocumentQuery<dynamic> query, string filterJson)
        {
            using var doc = JsonDocument.Parse(filterJson);

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
                                query.WhereGreaterThan(fieldName, value);
                                break;
                            case "$gte":
                                query.WhereGreaterThanOrEqual(fieldName, value);
                                break;
                            case "$lt":
                                query.WhereLessThan(fieldName, value);
                                break;
                            case "$lte":
                                query.WhereLessThanOrEqual(fieldName, value);
                                break;
                            case "$ne":
                                query.WhereNotEquals(fieldName, value);
                                break;
                            default:
                                query.WhereEquals(fieldName, value);
                                break;
                        }
                    }
                }
                else
                {
                    query.WhereEquals(fieldName, ExtractValue(property.Value));
                }
            }
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
