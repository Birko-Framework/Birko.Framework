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
                var (op, value) = assignment.Match(
                    set => ("=", set.Value),
                    increment => ("+=", (object?)increment.Delta));

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
    }
}
