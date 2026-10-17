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
                patchParts.Add($"this.{RqlField(kvp.Key)} = ${paramName}");
                parameters[paramName] = kvp.Value;
            }
            var patchScript = string.Join("; ", patchParts);

            var query = $"FROM {RqlCollection(collection)}";
            var (whereClause, whereParams) = ParseFilterToRql(filterJson);
            // SH-H032: an empty clause means either "no filter" or "every term was dropped" (e.g.
            // {"status":{}} takes the object branch and the operator loop adds nothing). Only the first is
            // a deliberate match-all; the second used to patch the whole collection.
            Data.Migrations.Context.MigrationFilter.RequireBounded(filterJson, whereClause.Length > 0,
                "update", collection, "every document in the collection");
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
            var query = $"FROM {RqlCollection(collection)}";
            var (whereClause, whereParams) = ParseFilterToRql(filterJson);
            // SH-H032 — see UpdateDocuments. This path used to send an unfiltered DeleteByQueryOperation.
            Data.Migrations.Context.MigrationFilter.RequireBounded(filterJson, whereClause.Length > 0,
                "delete", collection, "every document in the collection");
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
            // TASK-152, measured live: the first parameter is the INDEX name, so passing the collection there asked for an
            // index called after it and every count threw IndexDoesNotExistException.
            var query = session.Advanced.DocumentQuery<dynamic>(collectionName: collection);

            if (!Data.Migrations.Context.MigrationFilter.IsExplicitMatchAll(filterJson))
            {
                // SH-H032: guard the read too, so a count cannot silently answer for the whole collection
                // while a delete built from the same filter is refused (§ TASK-215, § TASK-313).
                var applied = ApplyFilterToQuery(query, filterJson!);
                Data.Migrations.Context.MigrationFilter.RequireBounded(filterJson, applied, "count",
                    collection, "every document in the collection");
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
            // TASK-152: terms from the one shared reader (SH-M175: an unknown operator was equality). RQL's `!=` already
            // matches a document without the field; `= null` does not (measured: 0), so null equality also takes the
            // absent field, as the filter's dialect means (SH-M176).
            var parameters = new Dictionary<string, object?>();
            var conditions = new List<string>();
            var index = 0;
            foreach (var term in MigrationFilterTerms.Parse(filterJson))
            {
                var field = RqlField(term.Field);
                if (term.Value == null)
                {
                    conditions.Add(term.Operator == MigrationFilterOperator.Equal
                        ? $"({field} = null or (true and not exists({field})))"
                        : $"(exists({field}) and {field} != null)");
                    continue;
                }

                var paramName = $"p{index++}";
                conditions.Add($"{field} {RqlOperator(term.Operator)} ${paramName}");
                parameters[paramName] = term.Value;
            }

            return (string.Join(" AND ", conditions), parameters);
        }

        private static string RqlOperator(MigrationFilterOperator op) => op switch
        {
            MigrationFilterOperator.Equal => "=",
            MigrationFilterOperator.NotEqual => "!=",
            MigrationFilterOperator.GreaterThan => ">",
            MigrationFilterOperator.GreaterThanOrEqual => ">=",
            MigrationFilterOperator.LessThan => "<",
            _ => "<=",
        };

        private static readonly System.Text.RegularExpressions.Regex RqlPath =
            new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$");

        /// <summary>
        /// A field name as it reaches RQL text. Values are bound; a field name cannot be, so it is held to a plain
        /// (dotted) identifier and anything else refused (rule 33): it was interpolated raw, so a crafted key in a
        /// delete filter rewrote the query.
        /// </summary>
        /// <remarks>
        /// TASK-640: an update key and <c>RenameField</c>'s two names reach the patch script the same way, so they are held
        /// to the same rule. Measured live: an update key <c>Role = 'admin'; this.Note</c> wrote <c>Role</c> as well.
        /// </remarks>
        internal static string RqlField(string field)
            => field != null && RqlPath.IsMatch(field)
                ? field
                : throw new NotSupportedException(
                    $"Migration field '{field}' is not a plain identifier path, so it cannot be put into an RQL query. Use Raw().");

        /// <summary>
        /// A collection name as it reaches RQL text: a quoted string literal, with <c>\</c> and <c>'</c> escaped.
        /// </summary>
        /// <remarks>
        /// TASK-640. A collection name cannot be a query parameter, and an apostrophe in one is legitimate, so it is escaped
        /// rather than refused. It was interpolated inside quotes raw: measured live,
        /// <c>DropCollection("Accounts' WHERE Name = 'b")</c> deleted only <c>b</c> from <c>Accounts</c>.
        /// </remarks>
        internal static string RqlCollection(string collection)
        {
            if (string.IsNullOrEmpty(collection))
                throw new ArgumentException("A migration needs a collection name.", nameof(collection));
            return "'" + collection.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        }

        /// <summary>
        /// Applies the filter to <paramref name="query"/> and reports whether it added any term at all
        /// (SH-H032). The count is what the guard reads, rather than a second parse of the JSON, so the
        /// refusal cannot disagree with the query that was actually built.
        /// </summary>
        private static bool ApplyFilterToQuery(IDocumentQuery<dynamic> query, string filterJson)
        {
            // TASK-152: the same terms as ParseFilterToRql, so the two RavenDB translations cannot disagree.
            var applied = 0;
            foreach (var term in MigrationFilterTerms.Parse(filterJson))
            {
                var field = RqlField(term.Field);
                if (term.Value == null)
                {
                    // As ParseFilterToRql: null equality takes the absent field too (SH-M176).
                    query.OpenSubclause();
                    if (term.Operator == MigrationFilterOperator.Equal)
                        query.WhereEquals(field, (object?)null).OrElse().Not.WhereExists(field);
                    else
                        query.WhereExists(field).AndAlso().WhereNotEquals(field, (object?)null);
                    query.CloseSubclause();
                    applied++;
                    continue;
                }

                switch (term.Operator)
                {
                    case MigrationFilterOperator.Equal: query.WhereEquals(field, term.Value); break;
                    case MigrationFilterOperator.NotEqual: query.WhereNotEquals(field, term.Value); break;
                    case MigrationFilterOperator.GreaterThan: query.WhereGreaterThan(field, term.Value); break;
                    case MigrationFilterOperator.GreaterThanOrEqual: query.WhereGreaterThanOrEqual(field, term.Value); break;
                    case MigrationFilterOperator.LessThan: query.WhereLessThan(field, term.Value); break;
                    default: query.WhereLessThanOrEqual(field, term.Value); break;
                }
                applied++;
            }

            return applied > 0;
        }

    }
}
