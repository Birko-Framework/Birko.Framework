using System;
using System.Collections.Generic;
using System.Linq;
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

            // CR-M114: parameterize the patch values ($uN) instead of interpolating them.
            var parameters = new Dictionary<string, object?>();
            var patchParts = new List<string>();
            var uIndex = 0;
            foreach (var kvp in updates)
            {
                var paramName = $"u{uIndex++}";
                patchParts.Add($"this.{kvp.Key} = ${paramName}");
                parameters[paramName] = kvp.Value;
            }
            var patchScript = string.Join("; ", patchParts);

            var query = $"FROM '{collection}'";
            var (whereClause, whereParams) = ParseFilterToRql(filterJson);
            if (!string.IsNullOrEmpty(whereClause))
            {
                query += $" WHERE {whereClause}";
                foreach (var kv in whereParams) parameters[kv.Key] = kv.Value;
            }

            query += $" UPDATE {{ {patchScript}; }}";

            var indexQuery = new Raven.Client.Documents.Queries.IndexQuery { Query = query, QueryParameters = new() };
            foreach (var kv in parameters) indexQuery.QueryParameters[kv.Key] = kv.Value;

            var operation = _store.Operations.Send(new PatchByQueryOperation(indexQuery));
            operation.WaitForCompletion();
        }

        public void DeleteDocuments(string collection, string filterJson)
        {
            var query = $"FROM '{collection}'";
            var (whereClause, whereParams) = ParseFilterToRql(filterJson);
            var indexQuery = new Raven.Client.Documents.Queries.IndexQuery { Query = query, QueryParameters = new() };
            if (!string.IsNullOrEmpty(whereClause))
            {
                query += $" WHERE {whereClause}";
                foreach (var kv in whereParams) indexQuery.QueryParameters[kv.Key] = kv.Value;
            }
            indexQuery.Query = query;

            var operation = _store.Operations.Send(new DeleteByQueryOperation(indexQuery));
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

            // Execute the query that was actually built so the collection scope and filter are honored.
            // Previously a separate, unscoped session.Query<dynamic>() was counted, returning the total
            // document count of the whole database regardless of collection/filter (CR-C12).
            query.Statistics(out var queryStats).Take(0).ToList();

            return queryStats.TotalResults;
        }

        public void CopyData(string sourceCollection, string targetCollection, string? transformJson = null)
        {
            // The previous implementation loaded documents from sourceCollection and re-stored them;
            // because RavenDB session-loaded documents retain their existing ids and @collection
            // metadata, Store + SaveChanges re-saved them into the SOURCE collection — targetCollection
            // and transformJson were ignored and nothing was copied (CR-C13). It also loaded the entire
            // collection into one session via Take(int.MaxValue).
            //
            // A correct cross-collection copy requires re-keying each document into targetCollection
            // (new id / @collection metadata) and applying transformJson, ideally via a server-side
            // PatchByQueryOperation. Rather than silently doing the wrong thing, fail fast until a
            // verified implementation lands.
            throw new NotSupportedException(
                "RavenDBDataMigrator.CopyData is not yet implemented. Cross-collection copy must re-key " +
                "documents into the target collection (and apply the transform); use a server-side " +
                "PatchByQueryOperation. Tracked as a follow-up to CODE-REVIEW-AUDIT CR-C13.");
        }

        public void BulkInsert(string collection, IEnumerable<IDictionary<string, object>> documents)
        {
            if (documents == null) return;

            using var bulkInsert = _store.BulkInsert();
            foreach (var doc in documents)
            {
                if (doc == null || doc.Count == 0) continue;
                // Force the requested collection via @collection metadata — storing a raw dictionary
                // otherwise lands documents in a generic collection derived from the CLR type, not
                // the caller's `collection`, leaving them unqueryable under that name (CR-H065).
                var metadata = new Raven.Client.Json.MetadataAsDictionary
                {
                    [Raven.Client.Constants.Documents.Metadata.Collection] = collection
                };
                bulkInsert.Store(doc, metadata);
            }
        }

        /// <summary>
        /// Translates a Mongo-style JSON filter into an RQL WHERE clause using <c>$pN</c> query
        /// parameters for the values (CR-M114: values used to be interpolated as `'{s}'` with no
        /// escaping, so a value containing a quote/backslash broke or altered the query). The returned
        /// parameters are keyed without the leading <c>$</c> (RavenDB QueryParameters convention).
        /// </summary>
        internal static (string Rql, IReadOnlyDictionary<string, object?> Parameters) ParseFilterToRql(string? filterJson)
        {
            var parameters = new Dictionary<string, object?>();
            if (string.IsNullOrWhiteSpace(filterJson) || filterJson!.Trim() == "{}")
                return (string.Empty, parameters);

            using var doc = JsonDocument.Parse(filterJson);
            var conditions = new List<string>();
            var index = 0;

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var fieldName = property.Name;

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var op in property.Value.EnumerateObject())
                    {
                        var rqlOp = op.Name switch
                        {
                            "$gt" => ">",
                            "$gte" => ">=",
                            "$lt" => "<",
                            "$lte" => "<=",
                            "$ne" => "!=",
                            _ => "="
                        };
                        var paramName = $"p{index++}";
                        conditions.Add($"{fieldName} {rqlOp} ${paramName}");
                        parameters[paramName] = ExtractValue(op.Value);
                    }
                }
                else
                {
                    var paramName = $"p{index++}";
                    conditions.Add($"{fieldName} = ${paramName}");
                    parameters[paramName] = ExtractValue(property.Value);
                }
            }

            return (string.Join(" AND ", conditions), parameters);
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

        internal static object? ExtractValue(JsonElement element)
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
