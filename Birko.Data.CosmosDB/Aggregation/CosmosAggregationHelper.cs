using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Birko.Data.CosmosDB.Aggregation
{
    /// <summary>
    /// Shared helper for building Cosmos DB SQL aggregation queries and parsing JSON results.
    /// Used by both <see cref="Stores.CosmosDBStore{T}"/> and <see cref="Stores.AsyncCosmosDBStore{T}"/>.
    /// </summary>
    public static class CosmosAggregationHelper
    {
        /// <summary>
        /// Returns the Cosmos DB SQL aggregate expression for the given function and field.
        /// For Count returns "COUNT(1)", for others returns "FUNC(fieldExpression)".
        /// Shared by store-level and view-level aggregation.
        /// </summary>
        public static string BuildFunctionSql(AggregateFunction function, string? fieldExpression)
        {
            return function switch
            {
                AggregateFunction.Count => "COUNT(1)",
                AggregateFunction.Sum => $"SUM({fieldExpression})",
                AggregateFunction.Avg => $"AVG({fieldExpression})",
                AggregateFunction.Min => $"MIN({fieldExpression})",
                AggregateFunction.Max => $"MAX({fieldExpression})",
                _ => throw new NotSupportedException($"Aggregate function {function} is not supported")
            };
        }

        /// <summary>
        /// Builds the SELECT and GROUP BY parts of a Cosmos DB SQL aggregation query.
        /// Returns them separately so callers can insert WHERE between them.
        /// Shared by store-level and view-level aggregation.
        /// </summary>
        /// <param name="groupByFields">Tuples of (fieldName, alias) for GROUP BY columns.</param>
        /// <param name="aggregates">Tuples of (function, sourceProperty, alias) for aggregate columns.</param>
        /// <returns>A tuple of (selectFromSql, groupBySql) where groupBySql is null if no GROUP BY fields.</returns>
        public static (string SelectFromSql, string? GroupBySql) BuildAggregateSqlParts(
            IEnumerable<(string FieldName, string Alias)> groupByFields,
            IEnumerable<(AggregateFunction Function, string? SourceProperty, string Alias)> aggregates)
        {
            var selectParts = new List<string>();
            var groupByFieldList = groupByFields.ToList();

            foreach (var (fieldName, alias) in groupByFieldList)
            {
                selectParts.Add($"{FieldRef(fieldName)} AS {alias}");
            }

            foreach (var (function, sourceProperty, alias) in aggregates)
            {
                var fieldExpr = function == AggregateFunction.Count ? null : FieldRef(sourceProperty);
                selectParts.Add($"{BuildFunctionSql(function, fieldExpr)} AS {alias}");
            }

            var selectFromSql = $"SELECT {string.Join(", ", selectParts)} FROM c";

            string? groupBySql = groupByFieldList.Count > 0
                ? $" GROUP BY {string.Join(", ", groupByFieldList.Select(g => FieldRef(g.FieldName)))}"
                : null;

            return (selectFromSql, groupBySql);
        }

        /// <summary>
        /// Emits a bracket-quoted Cosmos DB property accessor (<c>c["Field"]</c>) instead of the
        /// dotted form (<c>c.Field</c>). Bracket quoting handles field names that would be invalid or
        /// injectable as a dotted identifier (whitespace, reserved words, embedded quotes), and keeps
        /// SELECT/GROUP BY identifiers consistent (CR-M084). A null/empty field falls back to <c>c</c>.
        /// </summary>
        internal static string FieldRef(string? fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return "c";
            }
            // Cosmos string accessors follow JSON string escaping (backslash), same as string literals.
            var escaped = fieldName.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"c[\"{escaped}\"]";
        }

        /// <summary>
        /// Builds a complete Cosmos DB SQL aggregation query (SELECT ... FROM c GROUP BY ...).
        /// For queries that don't need WHERE/ORDER BY between SELECT and GROUP BY.
        /// </summary>
        public static string BuildAggregateSql<T>(AggregateQuery<T> query)
            where T : Data.Models.AbstractModel
        {
            var (selectFromSql, groupBySql) = BuildAggregateSqlParts(
                query.GroupByFields.Select(f => (f, f)),
                query.Aggregates.Select(a => (a.Function, (string?)a.SourcePropertyName, a.ResolvedAlias)));

            return selectFromSql + (groupBySql ?? "");
        }

        /// <summary>
        /// Parses a <see cref="JsonElement"/> document into a dictionary of .NET values.
        /// Handles Number, String, Boolean, Null, and falls back to ToString for other types.
        /// </summary>
        /// <param name="doc">The JSON element from a Cosmos DB query result.</param>
        /// <returns>A dictionary mapping property names to their .NET values.</returns>
        public static Dictionary<string, object?> ParseJsonResult(JsonElement doc)
        {
            var row = new Dictionary<string, object?>();
            foreach (var prop in doc.EnumerateObject())
            {
                row[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? (object)l : prop.Value.GetDouble(),
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.ToString()
                };
            }
            return row;
        }
    }
}
