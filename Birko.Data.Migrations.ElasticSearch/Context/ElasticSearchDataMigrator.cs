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
            var query = ResolveQuery(filterJson, "update", collection);

            var response = _client.UpdateByQuery<dynamic>(descriptor =>
            {
                descriptor.Index(collection);

                descriptor.Query(_ => query);

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
            var query = ResolveQuery(filterJson, "delete", collection);

            var response = _client.DeleteByQuery<dynamic>(descriptor =>
            {
                descriptor.Index(collection);
                descriptor.Query(_ => query);
                return descriptor;
            });

            EnsureValid(response, $"DeleteByQuery on index '{collection}'");
        }

        public long CountDocuments(string collection, string? filterJson = null)
        {
            // SH-H032: guard the read too, so a count cannot silently answer for the whole index while a
            // delete built from the same filter is refused (§ TASK-215, § TASK-313).
            var query = ResolveQuery(filterJson, "count", collection);

            var response = _client.Count<dynamic>(descriptor =>
            {
                descriptor.Index(collection);
                descriptor.Query(_ => query);
                return descriptor;
            });

            // SH-M197 (TASK-152): the one call in this file that skipped the check. A missing index or a failed request
            // answers Count = 0, which a migration guarded by "== 0" reads as an empty index (and re-seeds it).
            EnsureValid(response, $"Count on index '{collection}'");
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
            // A copy that stopped early (timed out, cancelled) passes IsValid; see ReindexHelper.IncompleteReason.
            var incomplete = Birko.Data.ElasticSearch.IndexManagement.ReindexHelper.IncompleteReason(reindexResponse);
            if (incomplete != null)
            {
                throw new InvalidOperationException(
                    $"ElasticSearch migration step failed: ReindexOnServer '{sourceCollection}' -> '{targetCollection}' {incomplete}.");
            }

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
        internal static void EnsureValid(IResponse response, string operation)
        {
            if (response == null || !response.IsValid)
            {
                throw new InvalidOperationException(
                    $"ElasticSearch migration step failed: {operation}. {response?.DebugInformation}",
                    response?.OriginalException);
            }

            // SH-M198 (TASK-152): a by-query call that hit version conflicts or per-document failures still answers a
            // VALID response, so a partly applied update or delete passed and the migration was recorded. BulkInsert
            // already inspected its item errors; these did not.
            var (conflicts, failures) = response switch
            {
                UpdateByQueryResponse u => (u.VersionConflicts, u.Failures?.Count ?? 0),
                DeleteByQueryResponse d => (d.VersionConflicts, d.Failures?.Count ?? 0),
                _ => (0L, 0),
            };
            if (conflicts > 0 || failures > 0)
            {
                throw new InvalidOperationException(
                    $"ElasticSearch migration step was only partly applied: {operation} reported {conflicts} version conflict(s) "
                    + $"and {failures} failure(s).");
            }
        }

        /// <summary>
        /// The one place an index-wide query is allowed to be produced (SH-H032). All three filtered
        /// operations route through it, so "no filter supplied" stays a deliberate <c>match_all</c> while a
        /// filter that was supplied and yielded nothing is refused -- previously both reached the same
        /// index-wide query, and only one of them meant to.
        /// </summary>
        private static QueryContainer ResolveQuery(string? filterJson, string operation, string collection)
        {
            if (MigrationFilter.IsExplicitMatchAll(filterJson))
                return new QueryContainer(new MatchAllQuery());

            var query = ParseFilter(filterJson!, out var termCount);
            MigrationFilter.RequireBounded(filterJson, termCount > 0, operation, collection,
                "every document in the index");
            return query;
        }

        /// <summary>
        /// Translates the Mongo-style JSON filter into a <c>bool.must</c> query, reporting how many terms it
        /// actually produced (SH-H032). An empty <c>must</c> is <b>match-all</b> in Elasticsearch, so a
        /// filter whose every term was dropped -- <c>{"status":{}}</c> takes the object branch and the
        /// operator loop adds nothing -- used to widen a DeleteByQuery to the whole index. The count is what
        /// the guard reads, rather than a second parse, so the refusal cannot disagree with the query built.
        /// <para>The former <c>QueryContainerDescriptor</c> parameter was never used and is gone.</para>
        /// </summary>
        private static QueryContainer ParseFilter(string filterJson, out int termCount)
        {
            // TASK-152: terms from the one shared reader (SH-M175). Null takes the filter dialect's meaning (SH-M176)
            // through `exists`: a TermQuery with a null value is CONDITIONLESS in NEST and is dropped from the request,
            // so {"x": null} would have left a must clause that constrains nothing.
            var mustClauses = new List<QueryContainer>();
            foreach (var term in MigrationFilterTerms.Parse(filterJson))
            {
                var field = term.Field;
                var exists = new QueryContainer(new ExistsQuery { Field = field });
                mustClauses.Add(term.Operator switch
                {
                    MigrationFilterOperator.Equal when term.Value == null
                        => new QueryContainer(new BoolQuery { MustNot = new[] { exists } }),
                    MigrationFilterOperator.NotEqual when term.Value == null => exists,
                    MigrationFilterOperator.Equal => new QueryContainer(new TermQuery { Field = field, Value = term.Value }),
                    MigrationFilterOperator.NotEqual => new QueryContainer(new BoolQuery
                    {
                        MustNot = new[] { new QueryContainer(new TermQuery { Field = field, Value = term.Value }) }
                    }),
                    MigrationFilterOperator.GreaterThan => new QueryContainer(new NumericRangeQuery
                    {
                        Field = field, GreaterThan = ToRangeBound(term.Value, field, "$gt")
                    }),
                    MigrationFilterOperator.GreaterThanOrEqual => new QueryContainer(new NumericRangeQuery
                    {
                        Field = field, GreaterThanOrEqualTo = ToRangeBound(term.Value, field, "$gte")
                    }),
                    MigrationFilterOperator.LessThan => new QueryContainer(new NumericRangeQuery
                    {
                        Field = field, LessThan = ToRangeBound(term.Value, field, "$lt")
                    }),
                    _ => new QueryContainer(new NumericRangeQuery
                    {
                        Field = field, LessThanOrEqualTo = ToRangeBound(term.Value, field, "$lte")
                    }),
                });
            }

            termCount = mustClauses.Count;
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
                // TASK-152 (with SH-M199): the field NAME is a param too. It was interpolated as `ctx._source.{name}`, so a
                // name with a quote, a dot or a space broke or altered the script.
                var paramName = $"p{i}";
                var fieldParam = $"f{i}";
                scriptParams[paramName] = updates[keys[i]];
                scriptParams[fieldParam] = keys[i];
                sourceParts.Add($"ctx._source[params.{fieldParam}] = params.{paramName}");
            }
            return string.Join("; ", sourceParts);
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
