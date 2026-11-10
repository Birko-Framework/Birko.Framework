using Birko.Data.Stores;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        /// For Count returns "COUNT(1)", for others "FUNC(IS_NULL(field) ? undefined : field)": over the field's non-null
        /// values only. Shared by store-level and view-level aggregation.
        /// </summary>
        /// <remarks>
        /// TASK-885: a null is a value to Cosmos DB's aggregates, not an absence. Measured on the emulator (vnext-preview,
        /// 2026-11-10), over a group holding a null: <c>SUM</c> and <c>AVG</c> answer undefined (SUM(Score) nothing where
        /// C# gives 140), and <c>MIN</c> answers null, which sorts below every number and string (MIN(Score) null where C#
        /// gives 10), on the store aggregate and on a grouped view alike; <c>MAX</c> was right. C# and every other provider
        /// skip nulls. A missing field is undefined, which every aggregate already skips, so the null is turned into
        /// undefined (<c>c.X ?? undefined</c> does not: <c>??</c> only replaces undefined). Measured with the conditional:
        /// SUM 140, MIN 10, AVG 35, MAX 60, also for decimal, double, string and DateTime members. Over no non-null value
        /// SUM answers 0, the others undefined; the store aggregate makes that SUM null (<see cref="CosmosAggregatePlan"/>).
        /// </remarks>
        public static string BuildFunctionSql(AggregateFunction function, string? fieldExpression)
            => BuildFunctionSql(function, fieldExpression, null);

        /// <summary>
        /// <see cref="BuildFunctionSql(AggregateFunction, string?)"/> over a member of <paramref name="memberType"/>: a MIN
        /// or MAX of a <see cref="DateTime"/> compares <see cref="WallClockKey"/>, not the stored text (TASK-897).
        /// </summary>
        internal static string BuildFunctionSql(AggregateFunction function, string? fieldExpression, Type? memberType)
        {
            var argument = fieldExpression;
            if (function is AggregateFunction.Min or AggregateFunction.Max && fieldExpression != null && memberType != null
                && (Nullable.GetUnderlyingType(memberType) ?? memberType) == typeof(DateTime))
            {
                argument = WallClockKey(fieldExpression);
            }

            return function switch
            {
                AggregateFunction.Count => "COUNT(1)",
                AggregateFunction.Sum => $"SUM({NonNull(argument, fieldExpression)})",
                AggregateFunction.Avg => $"AVG({NonNull(argument, fieldExpression)})",
                AggregateFunction.Min => $"MIN({NonNull(argument, fieldExpression)})",
                AggregateFunction.Max => $"MAX({NonNull(argument, fieldExpression)})",
                _ => throw new NotSupportedException($"Aggregate function {function} is not supported")
            };
        }

        /// <summary>
        /// TASK-897: a stored <see cref="DateTime"/> as text that orders as C# orders the value read back, and reads back
        /// as that value: <c>yyyy-MM-ddTHH:mm:ss.fffffff</c> and the stored suffix (<c>Z</c>, an offset, or none).
        /// </summary>
        /// <remarks>
        /// <para>A DateTime is stored as ISO 8601 text with as many fraction digits as it needs, so MIN/MAX of the text put
        /// <c>"…10:00:00.5Z"</c> before <c>"…10:00:00Z"</c> (<c>.</c> sorts before <c>Z</c>; measured on the emulator,
        /// vnext-preview, 2026-11-10, on the store and on a grouped view). The fraction is padded to seven digits.</para>
        /// <para>C# compares two DateTimes by their ticks whatever their kind, and a value stored with an offset reads back as
        /// the reader's local time, so the order is the stored wall clock, as the time bucket's
        /// (<see cref="CosmosAggregatePlan"/>) is; it is exact when the stored offset is the reader's, the case the time
        /// bucket assumes too. Cosmos DB's date functions are not used: they convert an offset to UTC, and answer nothing
        /// before 1601-01-01 (measured), where this key is plain text and orders 1500 before 1600.</para>
        /// <para>An aggregate cannot be wrapped in a function (<c>SUBSTRING(MIN(..), ..)</c> is a 400, "Compositions of
        /// aggregates and other expressions are not allowed"), so the key is the value itself: it parses back to the stored
        /// DateTime, kind included.</para>
        /// </remarks>
        internal static string WallClockKey(string x) => WallClockKey(x, withSuffix: true);

        /// <summary>
        /// TASK-897, TASK-899: a DateTime group key, for the store aggregate and the grouped view alike. C# groups two
        /// DateTimes by their ticks, whatever their kind; Cosmos DB groups the stored text, so a UTC, a local and an
        /// unspecified time of one wall clock were three groups (measured on both). The group is the wall clock
        /// (<see cref="WallClockKey(string)"/> without the suffix), and its key the group's least stored value, so it reads
        /// back as a DateTime with a kind.
        /// </summary>
        /// <returns>The expression to select under the key's name, and the one to group on.</returns>
        internal static (string Select, string GroupBy) DateTimeGroupKey(string column)
            => ($"MIN({NonNull(WallClockKey(column), column)})", WallClockKey(column, withSuffix: false));

        /// <summary>
        /// <see cref="WallClockKey(string)"/>, without the suffix when <paramref name="withSuffix"/> is false: the wall clock
        /// alone, which a DateTime group is grouped on (TASK-897).
        /// </summary>
        private static string WallClockKey(string x, bool withSuffix)
        {
            var suffixLength = $"(ENDSWITH({x}, \"Z\") ? 1 : (SUBSTRING({x}, LENGTH({x}) - 6, 1) IN (\"+\", \"-\") ? 6 : 0))";
            var fraction = $"(SUBSTRING({x}, 19, 1) = \".\" ? SUBSTRING({x}, 20, LENGTH({x}) - 20 - {suffixLength}) : \"\")";
            var suffix = withSuffix ? $", RIGHT({x}, {suffixLength})" : "";
            return $"CONCAT(LEFT({x}, 19), \".\", LEFT(CONCAT({fraction}, \"0000000\"), 7){suffix})";
        }

        /// <summary>
        /// TASK-897: the type an aggregate of <paramref name="member"/> answers in, as the in-memory aggregation answers it
        /// (<see cref="AggregateMath.ResultType"/>), or a refusal before any request. A SUM or AVG of a non-numeric member is
        /// refused with LINQ's own exception and message; a MIN or MAX of a member Cosmos DB does not order as C# does, by
        /// name.
        /// </summary>
        /// <remarks>
        /// A MIN/MAX is answered for a number, an enum (stored as its number), a bool, a Guid (its text is lowercase hex in
        /// field order, the order <see cref="Guid.CompareTo(Guid)"/> uses) and a DateTime (<see cref="WallClockKey"/>).
        /// A string is refused: Cosmos DB orders by code point, C# by culture (measured: <c>MIN</c> is <c>"B"</c> where C#
        /// answers <c>"a"</c>), the rule TASK-896 applies to a culture comparison in a filter. Any other type
        /// (<c>DateTimeOffset</c>, <c>TimeSpan</c>, <c>char</c>, an object) is refused too: its stored text does not order as
        /// the value.
        /// </remarks>
        /// <param name="function">The aggregate.</param>
        /// <param name="member">The member it reads.</param>
        /// <param name="owner">What is aggregated, for the message: the store's model or the view.</param>
        internal static Type ResultType(AggregateFunction function, PropertyInfo member, string owner)
        {
            var type = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
            if (function is not (AggregateFunction.Min or AggregateFunction.Max)) return AggregateMath.ResultType(function, type);
            if (IsNumeric(type) || type.IsEnum || type == typeof(bool) || type == typeof(Guid) || type == typeof(DateTime)) return type;
            throw new NotSupportedException(
                $"Cosmos DB aggregate of {owner} refused: Min/Max of {member.DeclaringType?.Name}.{member.Name}, a {type.Name}, "
                + "is not ordered by Cosmos DB as C# orders it"
                + (type == typeof(string) ? " (Cosmos DB compares strings by code point, C# by culture)" : "")
                + ". Read the rows and aggregate them in the caller. (TASK-897)");
        }

        /// <summary>
        /// TASK-897: the property an aggregate name means, found as the in-memory aggregation finds it: a public instance
        /// property, in any case, with its exception and message. The one lookup of every store aggregate name: group-by
        /// field, aggregate source, time column. The query names it by the property's own name.
        /// </summary>
        internal static PropertyInfo ResolveMember(Type type, string name, string role)
            => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
               ?? throw new ArgumentException($"Aggregate {role} '{name}' is not a property of {type.Name}.", nameof(name));

        /// <summary>
        /// TASK-897: a group-by member's type, refused where Cosmos DB does not group as C# does: a group is a JSON value,
        /// so a scalar (a number, an enum, a bool, a string, a Guid, a DateTime) groups as its value, an object or a list
        /// as its content, where C# groups an object by reference.
        /// </summary>
        internal static Type GroupKeyType(PropertyInfo member, string owner)
        {
            var type = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
            if (IsNumeric(type) || type.IsEnum || type == typeof(bool) || type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime))
                return type;
            throw new NotSupportedException(
                $"Cosmos DB aggregate of {owner} refused: group-by field {member.DeclaringType?.Name}.{member.Name} is a {type.Name}, "
                + "which Cosmos DB does not group as C# does. Group by a scalar member. (TASK-897)");
        }

        /// <summary>The numeric types <see cref="AggregateMath"/> sums: every integral type, float, double and decimal.</summary>
        private static bool IsNumeric(Type type)
            => !type.IsEnum && Type.GetTypeCode(type) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                   or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single
                   or TypeCode.Double or TypeCode.Decimal;

        /// <summary>TASK-885: the field's value, or undefined where it is null, so an aggregate skips it as C# does.</summary>
        internal static string NonNull(string? fieldExpression)
            => NonNull(fieldExpression, fieldExpression);

        /// <summary><paramref name="argument"/>, or undefined where the field it is computed from is null.</summary>
        private static string NonNull(string? argument, string? fieldExpression)
            => $"IS_NULL({fieldExpression}) ? undefined : {argument}";

        /// <summary>TASK-885: how many of the group's values of the field are not null (a missing field is not counted).</summary>
        internal static string NonNullCount(string fieldExpression) => $"COUNT({NonNull(fieldExpression)})";

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
            => BuildAggregateSqlParts(groupByFields, aggregates.Select(a => (a.Function, a.SourceProperty, a.Alias, (Type?)null)));

        /// <summary>
        /// <see cref="BuildAggregateSqlParts(IEnumerable{ValueTuple{string, string}}, IEnumerable{ValueTuple{AggregateFunction, string, string}})"/>
        /// with each source member's type, so a MIN/MAX of a DateTime compares <see cref="WallClockKey"/> (TASK-897). The view
        /// store's.
        /// </summary>
        internal static (string SelectFromSql, string? GroupBySql) BuildAggregateSqlParts(
            IEnumerable<(string FieldName, string Alias)> groupByFields,
            IEnumerable<(AggregateFunction Function, string? SourceProperty, string Alias, Type? MemberType)> aggregates)
            => BuildAggregateSqlParts(groupByFields, aggregates, Array.Empty<(string, string)>());

        /// <summary>
        /// <see cref="BuildAggregateSqlParts(IEnumerable{ValueTuple{string, string}}, IEnumerable{ValueTuple{AggregateFunction, string, string, Type}})"/>
        /// with <paramref name="dateTimeGroups"/> grouped on the wall clock (<see cref="DateTimeGroupKey"/>, TASK-899), the
        /// view store's.
        /// </summary>
        internal static (string SelectFromSql, string? GroupBySql) BuildAggregateSqlParts(
            IEnumerable<(string FieldName, string Alias)> groupByFields,
            IEnumerable<(AggregateFunction Function, string? SourceProperty, string Alias, Type? MemberType)> aggregates,
            IReadOnlyList<(string FieldName, string Alias)> dateTimeGroups)
        {
            var extraColumns = new List<(string Expression, string Alias)>();
            var extraGroupBy = new List<string>();
            foreach (var (fieldName, alias) in dateTimeGroups)
            {
                var (select, groupBy) = DateTimeGroupKey(FieldRef(fieldName));
                extraColumns.Add((select, alias));
                extraGroupBy.Add(groupBy);
            }
            return BuildAggregateSqlParts(groupByFields, aggregates, "c", extraColumns, extraGroupBy);
        }

        /// <summary>
        /// The SELECT and GROUP BY parts over the container named <paramref name="source"/>: <c>FROM source</c>, and every
        /// accessor <c>source["Field"]</c>. <paramref name="extraColumns"/> are selected after the aggregates, and
        /// <paramref name="extraGroupBy"/> grouped after the fields (TASK-885: the time bucket and the non-null counts).
        /// </summary>
        private static (string SelectFromSql, string? GroupBySql) BuildAggregateSqlParts(
            IEnumerable<(string FieldName, string Alias)> groupByFields,
            IEnumerable<(AggregateFunction Function, string? SourceProperty, string Alias, Type? MemberType)> aggregates,
            string source,
            IReadOnlyList<(string Expression, string Alias)> extraColumns,
            IReadOnlyList<string> extraGroupBy)
        {
            var selectParts = new List<string>();
            var groupByFieldList = groupByFields.ToList();

            foreach (var (fieldName, alias) in groupByFieldList)
            {
                selectParts.Add($"{FieldRef(fieldName, source)} AS {Alias(alias)}");
            }

            foreach (var (function, sourceProperty, alias, memberType) in aggregates)
            {
                var fieldExpr = function == AggregateFunction.Count ? null : FieldRef(sourceProperty, source);
                selectParts.Add($"{BuildFunctionSql(function, fieldExpr, memberType)} AS {Alias(alias)}");
            }

            foreach (var (expression, alias) in extraColumns)
            {
                selectParts.Add($"{expression} AS {Alias(alias)}");
            }

            var selectFromSql = $"SELECT {string.Join(", ", selectParts)} FROM {source}";

            var groupByParts = groupByFieldList.Select(g => FieldRef(g.FieldName, source)).Concat(extraGroupBy).ToList();
            string? groupBySql = groupByParts.Count > 0
                ? $" GROUP BY {string.Join(", ", groupByParts)}"
                : null;

            return (selectFromSql, groupBySql);
        }

        /// <summary>
        /// Emits a bracket-quoted Cosmos DB property accessor (<c>c["Field"]</c>) instead of the
        /// dotted form (<c>c.Field</c>). Bracket quoting handles field names that would be invalid or
        /// injectable as a dotted identifier (whitespace, reserved words, embedded quotes), and keeps
        /// SELECT/GROUP BY identifiers consistent (CR-M084). A null/empty field falls back to <c>c</c>.
        /// </summary>
        internal static string FieldRef(string? fieldName) => FieldRef(fieldName, "c");

        private static string FieldRef(string? fieldName, string source)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return source;
            }
            // Cosmos string accessors follow JSON string escaping (backslash), same as string literals.
            var escaped = fieldName.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"{source}[\"{escaped}\"]";
        }

        /// <summary>
        /// TASK-844: a SELECT alias written as a quoted string, <c>AS "Value"</c>, escaped as <see cref="FieldRef(string?)"/>
        /// escapes a field name. A bare alias is parsed as an identifier, so a property named like a keyword
        /// (<c>Inner</c>, <c>Value</c>) was a 400 syntax error on every query (measured on the emulator, vnext-preview,
        /// 2026-11-07: <c>c["Sub"] AS Inner</c> → SC1001). The quoted form names the result property exactly as written,
        /// any characters included. The one producer of every alias this helper and the view store write.
        /// </summary>
        internal static string Alias(string alias)
            => "\"" + alias.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>
        /// TASK-843: the bracket-quoted accessor of a member path, <c>["Sub", "X"]</c> → <c>c["Sub"]["X"]</c>, each
        /// segment quoted as <see cref="FieldRef(string?)"/> quotes one. A one-segment path is exactly
        /// <see cref="FieldRef(string?)"/>'s output.
        /// </summary>
        internal static string PathRef(IReadOnlyList<string> path)
        {
            var sb = new System.Text.StringBuilder("c");
            foreach (var segment in path)
            {
                // FieldRef(segment) is c["segment"]: the accessor is everything after the leading "c".
                var single = FieldRef(segment);
                sb.Append(single, 1, single.Length - 1);
            }
            return sb.ToString();
        }

        /// <summary>The alias the Cosmos DB LINQ provider gives the container in the text it renders.</summary>
        private const string LinqRoot = "root";

        /// <summary>What the LINQ provider renders for an unfiltered <c>GetItemLinqQueryable&lt;T&gt;()</c>.</summary>
        private const string LinqSelectAll = "SELECT VALUE root FROM root";

        /// <summary>What it renders in front of the predicate of <c>GetItemLinqQueryable&lt;T&gt;().Where(filter)</c>.</summary>
        private const string LinqWherePrefix = LinqSelectAll + " WHERE ";

        /// <summary>
        /// TASK-876: the store-level aggregation query, <c>SELECT … FROM root [WHERE …] [GROUP BY …]</c>, whose WHERE is
        /// the one the store's reads send.
        /// </summary>
        /// <remarks>
        /// <para>The method this replaces, <c>BuildAggregateSql</c>, took no filter and emitted no WHERE, so
        /// <c>Aggregate</c> / <c>AggregateAsync</c> aggregated the whole container whatever <c>query.Filter</c> said.
        /// Measured on the emulator (vnext-preview, 2026-11-09): <c>A &gt; 3</c> counted 6 rows of 6, and every filtered
        /// shape answered the unfiltered aggregate.</para>
        /// <para><b>One producer.</b> The predicate is not translated here. <paramref name="filtered"/> is what the store's
        /// own reads send, <c>GetItemLinqQueryable&lt;T&gt;().Where(filter).ToQueryDefinition()</c> after the same rewrites
        /// (<c>SpanContains</c>, <c>DateTruncation</c>, <c>NullOrderingFold</c>). Its text is
        /// <c>SELECT VALUE root FROM root WHERE &lt;predicate&gt;</c>; the predicate is taken from it with every parameter it
        /// carries, and the aggregate is written over the provider's own alias, <c>root</c>. A rendering of any other
        /// shape is refused by name, never guessed at: a filter is never dropped.</para>
        /// <para>TASK-885: every SUM also selects how many non-null values it saw, so a SUM over none answers null, as the
        /// in-memory aggregation does (Cosmos DB answers 0); a time bucket is grouped on as
        /// <see cref="CosmosAggregatePlan"/> describes. Those columns are read and removed by
        /// <see cref="CosmosAggregatePlan.Read"/>.</para>
        /// <para>TASK-897: every name is resolved by <see cref="ResolveMember"/> and each aggregate typed by
        /// <see cref="ResultType"/>; a decimal AVG is a hidden SUM and COUNT, a DateTime MIN/MAX and group compare
        /// <see cref="WallClockKey(string)"/>.</para>
        /// </remarks>
        /// <param name="query">The aggregation. Its filter is not read here; <paramref name="filtered"/> carries it.</param>
        /// <param name="filtered">The store's rendering of the rewritten filter, or <c>null</c> for no filter.</param>
        /// <exception cref="NotSupportedException">The rendering is not a plain WHERE over the container, the time bucket
        /// cannot be answered as the in-memory aggregation answers it, a SUM/AVG is of a non-numeric member (LINQ's
        /// message), or a MIN/MAX or group key is of a type Cosmos DB does not order or group as C# does.</exception>
        /// <exception cref="ArgumentException">A name is no property, or the time bucket is incomplete or does not parse.</exception>
        internal static CosmosAggregatePlan BuildAggregateQuery<T>(AggregateQuery<T> query, QueryDefinition? filtered)
            where T : Data.Models.AbstractModel
        {
            // TASK-897: every name is resolved as the in-memory aggregation resolves it, in its order: the group-by fields,
            // the time column, the aggregate sources. The query names each member by its own name; a result keeps the
            // name as the caller wrote it, as in memory.
            var owner = typeof(T).Name;
            var groups = query.GroupByFields
                .Select(name => (Name: name, Member: ResolveMember(typeof(T), name, "group-by field")))
                .Select(g => (g.Name, g.Member, Type: GroupKeyType(g.Member, owner)))
                .ToList();
            var bucket = CosmosAggregatePlan.ResolveTimeBucket<T>(query.TimeColumn, query.TimeBucketInterval);

            var used = new HashSet<string>(query.GroupByFields, StringComparer.Ordinal);
            used.UnionWith(query.Aggregates.Select(a => a.ResolvedAlias));
            used.Add(CosmosAggregatePlan.BucketAlias);

            var selected = new List<(AggregateFunction Function, string? SourceProperty, string Alias, Type? MemberType)>();
            var metrics = new List<CosmosAggregatePlan.Metric>();
            var extraColumns = new List<(string Expression, string Alias)>();
            var extraGroupBy = new List<string>();
            foreach (var aggregate in query.Aggregates)
            {
                var alias = aggregate.ResolvedAlias;
                if (aggregate.Function == AggregateFunction.Count)
                {
                    selected.Add((AggregateFunction.Count, null, alias, null));
                    metrics.Add(new CosmosAggregatePlan.Metric(alias, AggregateFunction.Count, typeof(int), null, null));
                    continue;
                }

                var member = ResolveMember(typeof(T), aggregate.SourcePropertyName, "aggregate source");
                var resultType = ResultType(aggregate.Function, member, owner);
                var field = FieldRef(member.Name, LinqRoot);
                if (aggregate.Function == AggregateFunction.Avg && resultType == typeof(decimal))
                {
                    // Cosmos DB averages in double (1/3 reads 0.333333333333333); C# divides the decimal SUM by the count.
                    var sumAlias = HiddenAlias(used);
                    var countOf = HiddenAlias(used);
                    extraColumns.Add(($"SUM({NonNull(field)})", sumAlias));
                    extraColumns.Add((NonNullCount(field), countOf));
                    metrics.Add(new CosmosAggregatePlan.Metric(alias, AggregateFunction.Avg, resultType, countOf, sumAlias));
                    continue;
                }

                selected.Add((aggregate.Function, member.Name, alias, member.PropertyType));
                string? countAlias = null;
                if (aggregate.Function == AggregateFunction.Sum)
                {
                    countAlias = HiddenAlias(used);
                    extraColumns.Add((NonNullCount(field), countAlias));
                }
                metrics.Add(new CosmosAggregatePlan.Metric(alias, aggregate.Function, resultType, countAlias, null));
            }

            string? timeCountAlias = null;
            if (bucket != null)
            {
                var column = FieldRef(bucket.Value.Member, LinqRoot);
                var ms = bucket.Value.Milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var expression = $"FLOOR(DateTimeToTimestamp(LEFT({column}, 19)) / {ms}) * {ms}";
                extraColumns.Add((expression, CosmosAggregatePlan.BucketAlias));
                extraGroupBy.Add(expression);
                timeCountAlias = HiddenAlias(used);
                extraColumns.Add((NonNullCount(column), timeCountAlias));
            }

            // TASK-897: C# groups two DateTimes by their ticks, whatever their kind; Cosmos DB groups the stored text, so a
            // local and an unspecified time of one wall clock were two groups (measured). A DateTime member is
            // grouped on its wall clock, and its key is the group's least stored value, so it reads back with a kind.
            var dateGroups = groups.Where(g => g.Type == typeof(DateTime)).ToList();
            foreach (var (name, member, _) in dateGroups)
            {
                var (select, groupBy) = DateTimeGroupKey(FieldRef(member.Name, LinqRoot));
                extraColumns.Add((select, name));
                extraGroupBy.Add(groupBy);
            }

            var (selectFromSql, groupBySql) = BuildAggregateSqlParts(
                groups.Where(g => g.Type != typeof(DateTime)).Select(g => (g.Member.Name, g.Name)),
                selected,
                LinqRoot,
                extraColumns,
                extraGroupBy);

            var where = filtered == null ? null : WhereOf<T>(filtered.QueryText);
            var definition = new QueryDefinition(selectFromSql + (where == null ? "" : " WHERE " + where) + (groupBySql ?? ""));
            if (filtered != null)
            {
                foreach (var (name, value) in filtered.GetQueryParameters())
                {
                    definition = definition.WithParameter(name, value);
                }
            }

            return new CosmosAggregatePlan(
                definition,
                owner,
                groups.Select(g => (g.Name, g.Type)).ToList(),
                metrics,
                bucket == null ? null : (bucket.Value.Member, timeCountAlias!));
        }

        /// <summary>A result column name no group field or aggregate alias uses.</summary>
        private static string HiddenAlias(HashSet<string> used)
        {
            var alias = "__birko_n" + used.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            while (!used.Add(alias)) alias += "_";
            return alias;
        }

        /// <summary>The predicate of a rendered <c>Where</c>, or <c>null</c> when the provider rendered none.</summary>
        private static string? WhereOf<T>(string linqText)
        {
            if (string.Equals(linqText, LinqSelectAll, StringComparison.Ordinal)) return null;
            if (linqText.Length > LinqWherePrefix.Length && linqText.StartsWith(LinqWherePrefix, StringComparison.Ordinal))
            {
                return linqText.Substring(LinqWherePrefix.Length);
            }
            throw new NotSupportedException(
                $"Cosmos DB aggregate of {typeof(T).Name} refused: its filter did not render as a plain WHERE over the "
                + "container, so it cannot be placed in front of the aggregation, and a filter is never dropped. "
                + "Aggregate the rows of a read instead. (TASK-876)");
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
