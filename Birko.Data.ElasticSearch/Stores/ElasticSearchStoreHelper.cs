using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.ElasticSearch.Stores
{
    /// <summary>
    /// Shared, store-agnostic helpers used by both the sync <see cref="ElasticSearchStore{T}"/> and the
    /// async <see cref="AsyncElasticSearchStore{T}"/>. Extracted to remove the copy-paste drift risk called
    /// out by CR-L113 — the index-name resolution/sanitization and the <c>PropertyUpdate</c>-to-Painless
    /// script builder were near-identical copies across the two stores.
    /// </summary>
    internal static class ElasticSearchStoreHelper
    {
        /// <summary>
        /// Whether an index create failed only because the index already exists (TASK-997): another client created it
        /// between this store's existence check and its create. Read from the server's error type, nothing else.
        /// </summary>
        public static bool IndexAlreadyExists(Nest.CreateIndexResponse response)
            => response.ServerError?.Error?.Type == "resource_already_exists_exception";

        /// <summary>How long a store waits for an index another client is creating to answer (TASK-997).</summary>
        public static readonly TimeSpan ConcurrentCreateWait = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Waits until an index another client may be creating can answer a search (TASK-997). Measured on a single node:
        /// the index is listed, and its health reads yellow, a moment before its shard serves a search. A count in that
        /// window fails with "all shards failed" (<c>no_shard_available_action_exception</c>). So this waits for yellow and
        /// then retries a count while that is its only error, up to <see cref="ConcurrentCreateWait"/>. A ready index
        /// answers at once.
        /// </summary>
        public static void WaitForIndexReady(Nest.IElasticClient client, string indexName)
        {
            var until = DateTime.UtcNow + ConcurrentCreateWait;
            RequireHealth(client.Cluster.Health(indexName, h => h
                .WaitForStatus(Elasticsearch.Net.WaitForStatus.Yellow)
                .Timeout(ConcurrentCreateWait)), indexName);
            while (true)
            {
                var count = client.Count<object>(c => c.Index(indexName));
                if (!ShardNotYetAvailable(count, indexName, until)) return;
                System.Threading.Thread.Sleep(50);
            }
        }

        /// <inheritdoc cref="WaitForIndexReady"/>
        public static async System.Threading.Tasks.Task WaitForIndexReadyAsync(Nest.IElasticClient client, string indexName, System.Threading.CancellationToken ct)
        {
            var until = DateTime.UtcNow + ConcurrentCreateWait;
            RequireHealth(await client.Cluster.HealthAsync(indexName, h => h
                .WaitForStatus(Elasticsearch.Net.WaitForStatus.Yellow)
                .Timeout(ConcurrentCreateWait), ct).ConfigureAwait(false), indexName);
            while (true)
            {
                var count = await client.CountAsync<object>(c => c.Index(indexName), ct).ConfigureAwait(false);
                if (!ShardNotYetAvailable(count, indexName, until)) return;
                await System.Threading.Tasks.Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        private static void RequireHealth(Nest.ClusterHealthResponse health, string indexName)
        {
            if (health.IsValid && !health.TimedOut) return;
            throw new InvalidOperationException(
                $"Index {indexName} did not become ready within {ConcurrentCreateWait.TotalSeconds:0} s. "
                + $"DebugInfo: {health.DebugInformation}", health.OriginalException);
        }

        // True while the count failed only because the new shard cannot serve yet, and there is time left; false when it
        // answered. Any other failure, or the deadline, throws.
        private static bool ShardNotYetAvailable(Nest.CountResponse count, string indexName, DateTime until)
        {
            if (count.IsValid) return false;
            var notYet = count.ServerError?.Error?.RootCause?.Any(c => c.Type == "no_shard_available_action_exception") == true;
            if (notYet && DateTime.UtcNow < until) return true;
            throw new InvalidOperationException(
                $"Index {indexName} could not answer a search within {ConcurrentCreateWait.TotalSeconds:0} s. "
                + $"DebugInfo: {count.DebugInformation}", count.OriginalException);
        }

        /// <summary>
        /// Resolves the ElasticSearch index name for <paramref name="type"/> from the store settings,
        /// applying the ElasticSearch naming rules (see <see cref="SanitizeIndexName"/>).
        /// </summary>
        public static string ResolveIndexName(Settings? settings, Type type)
        {
            if (settings == null)
            {
                throw new InvalidOperationException("Settings not initialized. Call SetSettings() first.");
            }

            if (string.IsNullOrWhiteSpace(settings.Name))
            {
                throw new InvalidOperationException("Settings.Name cannot be empty");
            }

            string indexName = settings.IndexSettings?.FirstOrDefault(x => x.TypeName == type.FullName)?.Name ?? type.Name;
            return SanitizeIndexName($"{settings.Name}_{indexName}");
        }

        /// <summary>
        /// TASK-851: the terms limit declared for <paramref name="type"/>'s index, or <c>null</c> for the process-wide
        /// default. Read per call from the store's own settings; nothing shared is written.
        /// </summary>
        public static int? ResolveMaxTermsCount(Settings? settings, Type type)
            => settings?.IndexSettings?.FirstOrDefault(x => x.TypeName == type.FullName)?.MaxTermsCount;

        /// <summary>
        /// Sanitizes a raw index name according to ElasticSearch rules:
        /// lowercase; cannot start with <c>_</c>, <c>-</c>, or <c>.</c>; cannot contain spaces or any of
        /// <c># \ / * ? " &lt; &gt; | ` , +</c>. Throws when the result is empty or exceeds 255 characters.
        /// </summary>
        public static string SanitizeIndexName(string rawName)
        {
            var sanitizedIndexName = rawName
                .ToLower()
                .Trim()
                .Replace(" ", "_")
                .Replace("#", "_")
                .Replace("\\", "_")
                .Replace("/", "_")
                .Replace("*", "_")
                .Replace("?", "_")
                .Replace("\"", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("|", "_")
                .Replace(",", "_")
                .Replace("+", "_")
                .Replace("`", "_");

            // Remove invalid starting characters
            while (sanitizedIndexName.StartsWith("_") ||
                   sanitizedIndexName.StartsWith("-") ||
                   sanitizedIndexName.StartsWith("."))
            {
                sanitizedIndexName = sanitizedIndexName.Substring(1);
            }

            if (string.IsNullOrWhiteSpace(sanitizedIndexName) || sanitizedIndexName.Length > 255)
            {
                throw new InvalidOperationException(
                    $"Invalid index name generated: '{sanitizedIndexName}'. " +
                    $"Index names must be 1-255 characters and cannot contain special characters.");
            }

            return sanitizedIndexName;
        }

        /// <summary>
        /// Throws unless a by-query write ran to completion: no version conflict, no failure, no timeout.
        /// </summary>
        /// <remarks>
        /// TASK-502. The response used to be discarded. Measured on 7.17.25: a conflict is a 409 with
        /// <c>IsValid == false</c> after a PARTIAL write (the rest of the matches never processed, because the default is
        /// <c>conflicts: abort</c>); a script error is a 400. <c>TimedOut</c> is checked on its own, because TASK-319
        /// measured the sibling reindex response answering 200 with <c>timed_out: true</c> and <c>IsValid</c> still true.
        /// </remarks>
        /// <exception cref="Data.Exceptions.StoreConcurrencyException">Documents changed underneath the write.</exception>
        /// <exception cref="Data.Exceptions.StoreException">Any other failure, or a timeout.</exception>
        public static void RequireComplete(Nest.UpdateByQueryResponse response, string index)
            => RequireComplete("update", index, response.IsValid, response.Updated, response.VersionConflicts,
                response.Failures?.Count ?? 0, response.TimedOut, response.DebugInformation, response.OriginalException);

        /// <inheritdoc cref="RequireComplete(Nest.UpdateByQueryResponse, string)"/>
        public static void RequireComplete(Nest.DeleteByQueryResponse response, string index)
            => RequireComplete("delete", index, response.IsValid, response.Deleted, response.VersionConflicts,
                response.Failures?.Count ?? 0, response.TimedOut, response.DebugInformation, response.OriginalException);

        private static void RequireComplete(string operation, string index, bool isValid, long applied, long conflicts,
            int failures, bool timedOut, string debug, Exception? inner)
        {
            if (conflicts > 0)
            {
                throw new Data.Exceptions.StoreConcurrencyException(
                    $"ElasticSearch {operation}-by-query on '{index}' stopped on {conflicts} version conflict(s) after applying "
                    + $"{applied} document(s); the remaining matches were not processed.",
                    applied, conflicts, inner);
            }
            if (!isValid || failures > 0)
            {
                throw new Data.Exceptions.StoreException(
                    $"ElasticSearch {operation}-by-query on '{index}' failed after applying {applied} document(s) "
                    + $"({failures} failure(s)). {debug}", inner);
            }
            if (timedOut)
            {
                throw new Data.Exceptions.StoreException(
                    $"ElasticSearch {operation}-by-query on '{index}' timed out after applying {applied} document(s); "
                    + "the remaining matches were not processed.");
            }
        }

        /// <summary>
        /// Builds the inline Painless script (and its parameters) that applies a
        /// <see cref="Data.Stores.PropertyUpdate{T}"/> as a native UpdateByQuery script.
        /// </summary>
        public static (string Script, Dictionary<string, object> Parameters) BuildUpdateScript<T>(
            Data.Stores.PropertyUpdate<T> updates)
            where T : Data.Models.AbstractModel
        {
            var scriptParts = new List<string>();
            var scriptParams = new Dictionary<string, object>();

            foreach (var assignment in updates.Assignments)
            {
                // TASK-501: resolved as ApplyTo and the MongoDB translator resolve it. A hard (MemberExpression) cast
                // threw InvalidCastException on a non-member selector, and a public field was written as a property.
                var property = assignment.PropertyInfo
                    ?? throw new ArgumentException($"Unable to resolve property from expression: {assignment.Property}");

                // The field the document is stored under: the producer the filter and sort translators use
                // (TASK-613), so a member renamed by its mapping attribute is written where it is read.
                var fieldName = Data.ElasticSearch.ElasticSearch.StoredFieldName(property, null)!;
                var paramName = "p_" + property.Name;
                if (assignment is Data.Stores.ComputedAssignment computedAssignment)
                {
                    // TASK-826: the formula in BigDecimal, so nothing passes through a double until the stored number.
                    var formula = RenderComputed(computedAssignment.Value, paramName, scriptParams);
                    scriptParts.Add($"ctx._source.{fieldName} = ({formula}).doubleValue()");
                    continue;
                }

                var (op, value) = assignment.Match(
                    set => ("=", set.Value),
                    increment => ("+=", (object?)increment.Delta),
                    computed => throw new InvalidOperationException("A computed set is rendered above."));

                if (value == null)
                {
                    // TASK-501: a literal null, never a parameter. This was bound as `value ?? ""`, so a string stored
                    // "" and a nullable number or date was handed a string, which the mapping refuses (measured: a 400
                    // that failed the whole update). An increment never gets here: its delta cannot be null.
                    scriptParts.Add($"ctx._source.{fieldName} {op} null");
                    continue;
                }

                scriptParts.Add($"ctx._source.{fieldName} {op} params.{paramName}");
                scriptParams[paramName] = value;
            }

            return (string.Join("; ", scriptParts), scriptParams);
        }

        /// <summary>
        /// TASK-826 — a computed set's formula as a Painless <c>BigDecimal</c> expression. A column is parsed from its
        /// stored text, a constant is a string parameter, a division keeps 34 significant digits (DECIMAL128), and
        /// <c>setScale</c> rounds with the mode named.
        /// </summary>
        private static string RenderComputed(Data.Stores.ComputedValue value, string paramName, Dictionary<string, object> scriptParams)
            => value.Match(
                column => $"new BigDecimal(String.valueOf(ctx._source.{Data.ElasticSearch.ElasticSearch.StoredFieldName(column.Property, null)}))",
                constant =>
                {
                    var name = paramName + "_" + scriptParams.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    while (scriptParams.ContainsKey(name)) name += "x";
                    scriptParams[name] = constant.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return $"new BigDecimal(params.{name})";
                },
                binary =>
                {
                    var left = RenderComputed(binary.Left, paramName, scriptParams);
                    var right = RenderComputed(binary.Right, paramName, scriptParams);
                    return binary.Operator switch
                    {
                        Data.Stores.ComputedOperator.Add => $"{left}.add({right})",
                        Data.Stores.ComputedOperator.Subtract => $"{left}.subtract({right})",
                        Data.Stores.ComputedOperator.Multiply => $"{left}.multiply({right})",
                        Data.Stores.ComputedOperator.Divide => $"{left}.divide({right}, MathContext.DECIMAL128)",
                        _ => throw new NotSupportedException($"Operator {binary.Operator} is not supported."),
                    };
                },
                round => $"{RenderComputed(round.Operand, paramName, scriptParams)}.setScale({round.Digits}, "
                    + (round.Mode == MidpointRounding.AwayFromZero ? "RoundingMode.HALF_UP" : "RoundingMode.HALF_EVEN") + ")");
    }
}
