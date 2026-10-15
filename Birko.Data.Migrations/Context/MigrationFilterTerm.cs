using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Birko.Data.Migrations.Context
{
    /// <summary>The comparisons a migration's JSON filter can express (TASK-152).</summary>
    public enum MigrationFilterOperator
    {
        Equal,
        NotEqual,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
    }

    /// <summary>One term of a migration's JSON filter: a field, an operator and a scalar value (or null).</summary>
    public sealed class MigrationFilterTerm
    {
        public MigrationFilterTerm(string field, MigrationFilterOperator op, object? value)
        {
            Field = field;
            Operator = op;
            Value = value;
        }

        public string Field { get; }
        public MigrationFilterOperator Operator { get; }

        /// <summary>A string, long, double, bool, or null. Null is only ever compared with Equal or NotEqual.</summary>
        public object? Value { get; }
    }

    /// <summary>
    /// The one reader of a migration's Mongo-style JSON filter, shared by the SQL, ElasticSearch, RavenDB and Cosmos DB
    /// data migrators (TASK-152).
    /// </summary>
    /// <remarks>
    /// <para>
    /// SH-M175: each backend kept its own copy of the parse, and each mapped an operator it did not know to equality
    /// (<c>_ =&gt; "="</c>) and an array value to its JSON text, so <c>{"tags":{"$in":["a","b"]}}</c> became
    /// <c>tags = '["a","b"]'</c> and <c>$exists</c> / <c>$regex</c> / <c>$not</c> became equality with their operand:
    /// an update or delete on the wrong rows, with no error. An operator outside <see cref="MigrationFilterOperator"/>,
    /// an array or object value, and an ordering comparison with null are now refused (rule 41).
    /// </para>
    /// <para>
    /// SH-M176: what null means is decided here once, as the filter's own dialect (MongoDB) defines it: <c>{"x": null}</c>
    /// matches a document whose <c>x</c> is null or absent, <c>{"x": {"$ne": null}}</c> one whose <c>x</c> has a value,
    /// and <c>{"x": {"$ne": v}}</c> also matches a document with no <c>x</c>. Each backend renders that meaning: SQL's
    /// <c>col = NULL</c> / <c>col &lt;&gt; NULL</c> matched nothing.
    /// </para>
    /// <para>
    /// An operator object with no operators (<c>{"status":{}}</c>) yields no term; <see cref="MigrationFilter.RequireBounded"/>
    /// still refuses the filter when nothing at all constrained it (SH-H032).
    /// </para>
    /// </remarks>
    public static class MigrationFilterTerms
    {
        private static readonly IReadOnlyDictionary<string, MigrationFilterOperator> Operators =
            new Dictionary<string, MigrationFilterOperator>(StringComparer.Ordinal)
            {
                ["$eq"] = MigrationFilterOperator.Equal,
                ["$ne"] = MigrationFilterOperator.NotEqual,
                ["$gt"] = MigrationFilterOperator.GreaterThan,
                ["$gte"] = MigrationFilterOperator.GreaterThanOrEqual,
                ["$lt"] = MigrationFilterOperator.LessThan,
                ["$lte"] = MigrationFilterOperator.LessThanOrEqual,
            };

        /// <summary>The terms of <paramref name="filterJson"/>, in document order; empty for null, blank or <c>{}</c>.</summary>
        /// <exception cref="NotSupportedException">An operator, a value or a null comparison the filter cannot express.</exception>
        /// <exception cref="ArgumentException">The filter is not a JSON object.</exception>
        public static IReadOnlyList<MigrationFilterTerm> Parse(string? filterJson)
        {
            var terms = new List<MigrationFilterTerm>();
            if (MigrationFilter.IsExplicitMatchAll(filterJson))
                return terms;

            using var doc = JsonDocument.Parse(filterJson!);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"A migration filter is a JSON object; got {doc.RootElement.ValueKind}.", nameof(filterJson));

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    terms.Add(new MigrationFilterTerm(property.Name, MigrationFilterOperator.Equal, Scalar(property.Value, property.Name, "$eq")));
                    continue;
                }

                foreach (var op in property.Value.EnumerateObject())
                {
                    if (!Operators.TryGetValue(op.Name, out var kind))
                        throw new NotSupportedException(
                            $"Migration filter operator '{op.Name}' on field '{property.Name}' is not supported; the supported ones are "
                            + string.Join(", ", Operators.Keys) + ". Use Raw() for anything else.");

                    var value = Scalar(op.Value, property.Name, op.Name);
                    if (value == null && kind is not (MigrationFilterOperator.Equal or MigrationFilterOperator.NotEqual))
                        throw new NotSupportedException(
                            $"Migration filter operator '{op.Name}' on field '{property.Name}' compares with null, which orders nothing.");
                    terms.Add(new MigrationFilterTerm(property.Name, kind, value));
                }
            }
            return terms;
        }

        private static object? Scalar(JsonElement element, string field, string op) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw new NotSupportedException(
                $"Migration filter value for '{field}' ({op}) is a JSON {element.ValueKind}; only a string, number, boolean or null "
                + "can be compared. Use Raw() for array or document matching."),
        };
    }
}
