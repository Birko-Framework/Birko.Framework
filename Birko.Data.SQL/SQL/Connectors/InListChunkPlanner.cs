using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.SQL.Conditions;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// What <see cref="InListChunkPlanner.Plan"/> decided for one statement: leave it as it is, split it into chunks, or
    /// refuse it.
    /// </summary>
    public sealed class InListChunkPlan
    {
        private InListChunkPlan(IReadOnlyList<IReadOnlyList<Condition>>? chunks, string? refusal, int chunkSize)
        {
            Chunks = chunks;
            Refusal = refusal;
            ChunkSize = chunkSize;
        }

        /// <summary>The statement runs as it is, as one statement.</summary>
        public static InListChunkPlan Unchanged { get; } = new(null, null, 0);

        internal static InListChunkPlan Chunked(IReadOnlyList<IReadOnlyList<Condition>> chunks, int chunkSize) => new(chunks, null, chunkSize);

        internal static InListChunkPlan Refused(string reason) => new(null, reason, 0);

        /// <summary>One condition list per chunk, each a copy of the caller's with the split IN cut to a non-empty slice.</summary>
        public IReadOnlyList<IReadOnlyList<Condition>>? Chunks { get; }

        /// <summary>Why the statement cannot run: it binds more parameters than the provider allows and cannot be split.</summary>
        public string? Refusal { get; }

        /// <summary>The number of list values per chunk; 0 when the statement is not chunked.</summary>
        public int ChunkSize { get; }

        public bool IsChunked => Chunks != null;

        public bool IsRefused => Refusal != null;

        /// <summary>The condition list of each statement to run: the chunks, or <paramref name="original"/> alone.</summary>
        public IEnumerable<IReadOnlyList<Condition>?> Statements(IReadOnlyList<Condition>? original)
            => Chunks ?? (IEnumerable<IReadOnlyList<Condition>?>)new[] { original };

        /// <summary>Throws the refusal as a <see cref="NotSupportedException"/>. Callers raise it before <c>DoCommand</c>.</summary>
        public void ThrowIfRefused()
        {
            if (Refusal != null) throw new NotSupportedException(Refusal);
        }
    }

    /// <summary>
    /// TASK-833. Splits a statement whose <c>ids.Contains(x.Col)</c> list is longer than the provider can bind (every
    /// parameter of the command counted), or longer than a chunk size that keeps it fast, into several statements, each
    /// with a slice of the list. Pure: it builds nothing and runs nothing; the parameter count of a probe build comes in as
    /// a function.
    /// </summary>
    /// <remarks>
    /// <para><b>When an IN can be split.</b> <c>IN(L) AND R</c> is the union over the chunks of <c>IN(Lk) AND R</c>, and that
    /// is the only identity used. So the IN must be reached from the root through AND alone: no root term after the first
    /// joins with OR, no group on the path is an OR group or negated, and the leaf is not a NOT IN and not column-to-column.
    /// A NOT IN is never split (the user's decision): "not in chunk 1" deleted on its own deletes chunk 2's rows. The one OR
    /// group that splits is <c>IN(L) OR c IS NULL</c> (TASK-859, a list holding <c>null</c>): its NULL leg goes with the first
    /// chunk only.</para>
    /// <para><b>Values.</b> Guid, numeric, enum, date/time and bool lists are split, and so is a string list that compares
    /// exactly (<see cref="Condition.Comparison"/> <see cref="StringComparison.Ordinal"/>: every list the expression parser
    /// and the rule converter build, TASK-873 / TASK-895). TASK-895 (the user's decision, 2026-11-10) reverses TASK-833's
    /// refusal for those: since TASK-873 a row equals at most one distinct C# string of the list, so the chunks are disjoint.
    /// A string list built by hand compares by the column's collation, under which two distinct C# strings can be one
    /// database value (a row would match two chunks, a count would over-count and an increment would apply twice), so it is
    /// still refused past the limit, and so is a <c>char</c> list. Duplicates are dropped, so .NET equality must be the
    /// database's: a list mixing value types is refused, and so is a <see cref="DateTimeOffset"/> list holding one instant at
    /// two offsets (equal in .NET, two values in SQLite).</para>
    /// <para><b>Statements that are not a union.</b> A sort, a limit or an offset makes the chunked read a different query
    /// (a global sort and page), and an aggregate's groups span chunks. The caller states such a reason; past the limit
    /// the statement is refused with it, below the limit nothing changes.</para>
    /// <para><b>Nothing that works today starts refusing.</b> A shape that cannot be split stays one statement while it is
    /// within the maximum; only past the maximum is it refused, where the driver would have failed anyway. The refusal
    /// names the operation, the parameter count, the maximum, the column and the reason, never a value (rule 42).</para>
    /// </remarks>
    public static class InListChunkPlanner
    {
        private sealed class Leaf
        {
            public Leaf(Condition condition, int[] path, int count, string? shapeReason, bool hasNullLeg = false)
            {
                Condition = condition;
                Path = path;
                Count = count;
                ShapeReason = shapeReason;
                HasNullLeg = hasNullLeg;
            }

            /// <summary>
            /// TASK-859: the IN is the first term of <c>IN(L) OR operand IS NULL</c> (a list that held <c>null</c>). The leg
            /// stays in the first chunk only, so the chunks stay disjoint: a NULL row is matched once, never once per chunk.
            /// </summary>
            public bool HasNullLeg { get; }

            public Condition Condition { get; }

            /// <summary>Root index, then the child index at each group level.</summary>
            public int[] Path { get; }

            /// <summary>The parameters the leaf binds today: every value, duplicates included.</summary>
            public int Count { get; }

            public string? ShapeReason { get; }
        }

        /// <param name="operation">Named in a refusal: read, count, delete, update, aggregate.</param>
        /// <param name="conditions">The statement's root condition list. Never mutated.</param>
        /// <param name="maxParameters">The provider's ceiling on parameters in one command.</param>
        /// <param name="preferredChunkSize">The list length past which a splittable list is chunked for speed.</param>
        /// <param name="countParameters">The parameter count of the statement built with the given conditions. Called once,
        /// and only when the lists are longer than <paramref name="preferredChunkSize"/>.</param>
        /// <param name="statementReason">Why this statement is not a union of chunks (sort, limit, offset, aggregate), or
        /// null when it is.</param>
        /// <param name="writtenColumns">The columns an UPDATE's SET writes, as the statement names them
        /// (<see cref="SetClauses.WrittenBy"/>). A list whose operand reads one of them is never split: the chunks run one
        /// after another, so a row the first chunk moves into a later chunk's values would be matched again (an increment
        /// applied twice, measured).</param>
        /// <param name="writtenColumnsUnknown">True for an UPDATE whose written columns are not known (SET clauses handed in
        /// already rendered); none of its lists is split.</param>
        public static InListChunkPlan Plan(
            string operation,
            IReadOnlyList<Condition>? conditions,
            int maxParameters,
            int preferredChunkSize,
            Func<IReadOnlyList<Condition>, int> countParameters,
            string? statementReason = null,
            IEnumerable<string>? writtenColumns = null,
            bool writtenColumnsUnknown = false)
        {
            if (countParameters == null) throw new ArgumentNullException(nameof(countParameters));
            if (preferredChunkSize < 1) throw new ArgumentOutOfRangeException(nameof(preferredChunkSize));
            if (conditions == null || conditions.Count == 0) return InListChunkPlan.Unchanged;

            var leaves = new List<Leaf>();
            var rootHasOr = conditions.Skip(1).Any(c => c.IsOr);
            for (var i = 0; i < conditions.Count; i++)
            {
                Collect(conditions[i], new[] { i }, rootHasOr ? "it is joined to the rest of the filter with OR" : null, leaves);
            }
            // Below the chunk size there is nothing to gain and no limit to meet, and no probe is built.
            if (leaves.Sum(l => (long)l.Count) <= preferredChunkSize) return InListChunkPlan.Unchanged;

            // A written column is one name, perhaps qualified: its last part.
            var written = new HashSet<string>((writtenColumns ?? Enumerable.Empty<string>())
                .Select(column => Identifiers(column).LastOrDefault() ?? column), StringComparer.OrdinalIgnoreCase);
            var ordered = leaves.OrderByDescending(l => l.Count).ToList();
            Leaf? chosen = null;
            List<object?>? distinct = null;
            string? firstReason = null;
            foreach (var leaf in ordered)
            {
                var reason = statementReason ?? leaf.ShapeReason
                    ?? (writtenColumnsUnknown
                        ? "the update's SET clauses were handed in rendered, so the columns it writes are not known, and a row one chunk changes could be matched by a later chunk"
                        : null)
                    ?? (Identifiers(leaf.Condition.Name ?? string.Empty).Any(written.Contains)
                        ? "the update writes the filtered column, so a row one chunk changes could be matched by a later chunk"
                        : null)
                    ?? ValueReason(leaf.Condition, out distinct);
                if (reason == null)
                {
                    chosen = leaf;
                    break;
                }
                firstReason ??= reason;
            }

            var probeLeaf = chosen ?? ordered[0];
            var overhead = countParameters(Replace(conditions, probeLeaf.Path, new[] { First(probeLeaf.Condition.Values) })) - 1;
            var total = (long)overhead + probeLeaf.Count;

            if (chosen == null || distinct == null)
            {
                if (total <= maxParameters) return InListChunkPlan.Unchanged;
                return InListChunkPlan.Refused(Message(operation, total, maxParameters, probeLeaf.Condition.Name, firstReason!));
            }

            var chunkSize = (int)Math.Min(preferredChunkSize, (long)maxParameters - overhead);
            if (chunkSize < 1)
            {
                return InListChunkPlan.Refused(Message(operation, total, maxParameters, chosen.Condition.Name,
                    $"the statement's other {overhead} parameters leave no room for any of it"));
            }
            if (distinct.Count <= chunkSize && total <= maxParameters) return InListChunkPlan.Unchanged;

            var chunks = new List<IReadOnlyList<Condition>>((distinct.Count + chunkSize - 1) / chunkSize);
            for (var start = 0; start < distinct.Count; start += chunkSize)
            {
                var slice = distinct.GetRange(start, Math.Min(chunkSize, distinct.Count - start)).ToArray();
                chunks.Add(Replace(conditions, chosen.Path, slice, dropNullLeg: chosen.HasNullLeg && start > 0));
            }
            return InListChunkPlan.Chunked(chunks, chunkSize);
        }

        private static string Message(string operation, long total, int max, string? column, string reason)
            => $"The {operation} binds {total} parameters, more than this provider's maximum of {max}, and its IN list on "
                + $"'{column}' cannot be split into chunks: {reason}. Split the list in the caller, or narrow the filter. (TASK-833)";

        /// <summary>
        /// Every identifier <paramref name="sql"/> names, unquoted, each part of a qualified name on its own:
        /// <c>"T".[Col]</c> → <c>T</c>, <c>Col</c>; <c>(T.Score + 0)</c> → <c>T</c>, <c>Score</c>. A list's operand is a
        /// column or, since TASK-213, a rendered value fragment (arithmetic, COALESCE, CASE) that may read several columns,
        /// so the question is whether it reads a written column at all (TASK-849). String literals and parameters are
        /// skipped. It over-approximates (a keyword or table name that equals a written column keeps the list whole), which
        /// costs only a split; missing a column would apply the update twice.
        /// </summary>
        internal static IEnumerable<string> Identifiers(string sql)
        {
            var i = 0;
            while (i < sql.Length)
            {
                var c = sql[i];
                if (c == '\'')
                {
                    i = SkipQuoted(sql, i, '\'');
                }
                else if (c == '"' || c == '`' || c == '[')
                {
                    var close = c == '[' ? ']' : c;
                    var end = SkipQuoted(sql, i, close);
                    var inner = sql.Substring(i + 1, Math.Max(0, end - i - 2));
                    yield return inner.Replace(new string(close, 2), new string(close, 1));
                    i = end;
                }
                else if (c == '@' || c == ':' || c == '$' || c == '?')
                {
                    // A parameter marker and its name.
                    i++;
                    while (i < sql.Length && IsIdentifierPart(sql[i])) i++;
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < sql.Length && IsIdentifierPart(sql[i])) i++;
                    yield return sql.Substring(start, i - start);
                }
                else if (char.IsDigit(c))
                {
                    // A number, including `1e5` and `0x1F`: never an identifier.
                    while (i < sql.Length && (IsIdentifierPart(sql[i]) || sql[i] == '.')) i++;
                }
                else
                {
                    i++;
                }
            }
        }

        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

        /// <summary>The index just past the quote that closes the one at <paramref name="open"/>; a doubled quote is an escape.</summary>
        private static int SkipQuoted(string sql, int open, char close)
        {
            var i = open + 1;
            while (i < sql.Length)
            {
                if (sql[i] == close)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == close)
                    {
                        i += 2;
                        continue;
                    }
                    return i + 1;
                }
                i++;
            }
            return sql.Length;
        }

        private static void Collect(Condition node, int[] path, string? inherited, List<Leaf> leaves)
        {
            var children = node.SubConditions?.ToList();
            if (children != null && IsInWithNullLeg(node, children))
            {
                // TASK-859: `IN(L) OR c IS NULL` is the union of `IN(L1) OR c IS NULL` and `IN(Lk)` for the other chunks,
                // and they are disjoint (a NULL is in no list), so it splits as a plain IN does.
                // A NOT IN with its leg (`NOT IN(L) OR c IS NULL`, C#'s `!ids.Contains(x.C)`) is still a NOT IN, never split.
                var inCount = Count(children[0].Values);
                if (inCount > 0)
                    leaves.Add(new Leaf(children[0], Append(path, 0), inCount, inherited ?? (children[0].IsNot ? NotInReason : null),
                        hasNullLeg: true));
                return;
            }
            if (children != null && children.Count > 0)
            {
                var reason = inherited
                    ?? (node.IsNot ? "it sits inside a negated group" : null)
                    ?? (node.IsOr ? "it sits inside an OR group" : null);
                for (var i = 0; i < children.Count; i++)
                {
                    Collect(children[i], Append(path, i), reason, leaves);
                }
                return;
            }

            // A column-to-column IN binds no parameters; an empty IN renders `1 = 0` and is never chunked.
            if (node.Type != ConditionType.In || node.IsField) return;
            var count = Count(node.Values);
            if (count == 0) return;
            var shape = inherited ?? (node.IsNot ? NotInReason : null);
            leaves.Add(new Leaf(node, path, count, shape));
        }

        private const string NotInReason = "it is a NOT IN, which is never split: each chunk's NOT IN alone matches the other chunks' rows";

        private static int[] Append(int[] path, int index)
        {
            var next = new int[path.Length + 1];
            path.CopyTo(next, 0);
            next[path.Length] = index;
            return next;
        }

        private static int Count(IEnumerable? values)
        {
            if (values == null) return 0;
            if (values is ICollection collection) return collection.Count;
            var n = 0;
            foreach (var _ in values) n++;
            return n;
        }

        private static object? First(IEnumerable? values)
        {
            foreach (var value in values!) return value;
            return null;
        }

        /// <summary>
        /// Null when the list can be split, with its distinct values in list order; otherwise the reason it cannot.
        /// </summary>
        private static string? ValueReason(Condition leaf, out List<object?>? distinct)
        {
            distinct = null;
            var values = leaf.Values;
            // TASK-895: an exact string list matches each row by at most one of its distinct (ordinal) values.
            var exactText = leaf.Comparison == StringComparison.Ordinal;
            var result = new List<object?>();
            var seen = new HashSet<object?>();
            var instants = new Dictionary<long, TimeSpan>();
            DateTimeKind? kind = null;
            Type? type = null;
            foreach (var value in values!)
            {
                if (value != null)
                {
                    var valueType = value.GetType();
                    if (valueType == typeof(char) || (valueType == typeof(string) && !exactText))
                    {
                        return "its values are strings compared by the column's collation (a condition built by hand), and under a "
                            + "case- or accent-insensitive collation two distinct strings can be one database value, so a row could "
                            + "match two chunks";
                    }
                    if (valueType != typeof(string) && !IsSplittableType(valueType))
                    {
                        return $"its values are of type {valueType.Name}; only Guid, numeric, enum, date/time and bool lists are split";
                    }
                    if (type == null)
                    {
                        type = valueType;
                    }
                    else if (type != valueType)
                    {
                        return "its values mix types, so duplicates cannot be told apart the way the database does";
                    }
                    // .NET tells 100 ns ticks apart; PostgreSQL timestamp and MySQL DATETIME(6) store microseconds, and the
                    // drivers round or truncate, so two such values can be one stored value in two chunks.
                    if ((value is DateTime dt && dt.Ticks % 10 != 0) || (value is DateTimeOffset dto && dto.Ticks % 10 != 0))
                    {
                        return "its date/time values carry sub-microsecond ticks, which some providers store as one value";
                    }
                    if (value is DateTime withKind)
                    {
                        if (kind != null && kind != withKind.Kind)
                        {
                            return "its DateTime values mix kinds, which providers convert differently";
                        }
                        kind = withKind.Kind;
                    }
                    if (value is DateTimeOffset offset)
                    {
                        if (instants.TryGetValue(offset.UtcTicks, out var known) && known != offset.Offset)
                        {
                            return "it holds one instant at two offsets, one value to some providers and two to others";
                        }
                        instants[offset.UtcTicks] = offset.Offset;
                    }
                }
                if (seen.Add(value)) result.Add(value);
            }
            distinct = result;
            return null;
        }

        private static bool IsSplittableType(Type type)
        {
            if (type.IsEnum) return true;
            return type == typeof(Guid) || type == typeof(bool)
                || type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
                || type == typeof(float) || type == typeof(double) || type == typeof(decimal)
                || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly)
                || type == typeof(TimeOnly) || type == typeof(TimeSpan);
        }

        /// <summary>
        /// A copy of <paramref name="root"/> in which only the nodes on <paramref name="path"/> are new, and the leaf at its
        /// end carries <paramref name="values"/>. Every other node is the caller's, shared, and none is written to.
        /// </summary>
        private static IReadOnlyList<Condition> Replace(IReadOnlyList<Condition> root, int[] path, object?[] values, bool dropNullLeg = false)
        {
            var copy = root.ToList();
            copy[path[0]] = Rebuild(root[path[0]], path, 1, values, dropNullLeg);
            return copy;
        }

        private static Condition Rebuild(Condition node, int[] path, int depth, object?[] values, bool dropNullLeg)
        {
            if (depth == path.Length)
            {
                return node.Copy(values, node.SubConditions);
            }
            var children = node.SubConditions!.ToList();
            children[path[depth]] = Rebuild(children[path[depth]], path, depth + 1, values, dropNullLeg);
            // The IN's own group, `IN(L) OR c IS NULL`: a chunk after the first keeps the IN alone.
            if (dropNullLeg && depth == path.Length - 1)
                children = children.Take(1).ToList();
            return node.Copy(node.Values, children);
        }

        /// <summary>
        /// TASK-859: <c>[NOT] IN(L) OR c IS NULL</c> over one operand — what the parser writes for a list that held <c>null</c>
        /// (or a <c>NOT IN</c> over a nullable operand), or
        /// a caller's <c>ids.Contains(x.C) || x.C == null</c>.
        /// </summary>
        private static bool IsInWithNullLeg(Condition node, IReadOnlyList<Condition> children)
            => node.IsOr && !node.IsNot && children.Count == 2
                && children[0].SubConditions?.Any() != true && children[0].Type == ConditionType.In
                && !children[0].IsField
                && children[1].SubConditions?.Any() != true && children[1].Type == ConditionType.IsNull && !children[1].IsNot
                && !string.IsNullOrEmpty(children[0].Name)
                && string.Equals(children[0].Name, children[1].Name, StringComparison.Ordinal);
    }
}
