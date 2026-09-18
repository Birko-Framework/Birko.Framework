using System;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;

namespace Birko.Data.SQL.Caching
{
    /// <summary>
    /// Builds deterministic cache keys for SQL store queries.
    /// Keys follow the format: sql:{scope}:{table}:{filterHash}:{orderHash}:{limit}:{offset}
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>SH-H005 — the <c>scope</c> segment.</b> Keys used to be table-relative only, so two stores
    /// pointed at different databases but sharing one <c>ICache</c> computed byte-identical keys and each
    /// served the other's rows. The scope is the database identity (<c>Settings.GetId()</c>), and it is
    /// the <b>outermost</b> segment so <see cref="GetTablePrefix"/> stays a usable invalidation prefix.
    /// </para>
    /// <para>
    /// <b>SH-H004 — why <see cref="TryDescribeFilter"/> exists.</b> A filter's <c>ToString()</c> is not
    /// automatically a value-distinguishing description of the query, so it cannot be keyed on blindly.
    /// That method is the single place which decides whether a filter <i>can</i> be keyed; callers must
    /// not compose a key from a raw <c>filter.ToString()</c>.
    /// </para>
    /// </remarks>
    public static class SqlCacheKeyBuilder
    {
        private const string Prefix = "sql";

        /// <summary>
        /// Marker that <see cref="ConstantExpression"/> renders when a value's own <c>ToString()</c> does
        /// not describe it — a closure display class, a collection, any type without a value-bearing
        /// <c>ToString</c>. Its presence means the rendered filter does not distinguish the query.
        /// </summary>
        private const string OpaqueValueMarker = "value(";

        /// <summary>
        /// Builds a cache key from the query components.
        /// </summary>
        /// <param name="scope">
        /// Identity of the database the query runs against — <c>Settings.GetId()</c>. SH-H005: without
        /// this, two stores on different databases sharing one cache serve each other's rows.
        /// </param>
        /// <param name="tableName">The SQL table name.</param>
        /// <param name="filterString">
        /// A <b>value-distinguishing</b> description of the filter, from <see cref="TryDescribeFilter"/>,
        /// or null for no filter. ⚠ Do not pass a raw <c>filter.ToString()</c>: see SH-H004 on that method.
        /// </param>
        /// <param name="orderString">String representation of the order clause, or null.</param>
        /// <param name="limit">Optional limit value.</param>
        /// <param name="offset">Optional offset value.</param>
        /// <returns>A deterministic cache key string.</returns>
        public static string BuildKey(string scope, string tableName, string? filterString, string? orderString, int? limit, int? offset)
        {
            var filterHash = string.IsNullOrEmpty(filterString) ? "_" : ComputeHash(filterString!);
            var orderHash = string.IsNullOrEmpty(orderString) ? "_" : ComputeHash(orderString!);
            var limitPart = limit?.ToString() ?? "_";
            var offsetPart = offset?.ToString() ?? "_";

            return $"{GetTablePrefix(scope, tableName)}{filterHash}:{orderHash}:{limitPart}:{offsetPart}";
        }

        /// <summary>
        /// Gets the table-level cache key prefix for invalidation, within one database.
        /// All cache keys for a given (scope, table) pair start with this prefix.
        /// </summary>
        /// <param name="scope">Identity of the database — <c>Settings.GetId()</c>.</param>
        /// <param name="tableName">The SQL table name.</param>
        /// <returns>The prefix string used for bulk invalidation.</returns>
        /// <remarks>
        /// SH-H005: the scope segment narrows invalidation as well as lookup, and the two must move
        /// together. Before it, a write through one store removed the <i>other</i> database's entries —
        /// over-invalidation, which was harmless (a spurious miss) but is the reason this prefix cannot be
        /// scoped without scoping <see cref="BuildKey"/> in the same change: narrowing only the prefix
        /// would leave entries nothing ever invalidates, which is worse than the leak being closed.
        /// </remarks>
        public static string GetTablePrefix(string scope, string tableName)
        {
            return $"{Prefix}:{Sanitize(scope)}:{tableName}:";
        }

        /// <summary>
        /// Produces a <b>value-distinguishing</b> description of a filter, or reports that it has none.
        /// </summary>
        /// <param name="filter">The filter expression, or null.</param>
        /// <param name="description">
        /// The description to key on. Null when <paramref name="filter"/> is null (meaning "no filter",
        /// which is itself a distinct and cacheable query).
        /// </param>
        /// <returns>
        /// <c>true</c> when the filter can be keyed; <c>false</c> when it cannot, in which case the caller
        /// MUST NOT cache — neither read from nor write to the cache — for that query.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>SH-H004.</b> The store used to key on <c>filter.ToString()</c> of the <b>raw</b> expression.
        /// A captured local renders as <c>value(&lt;&gt;c__DisplayClass0_0).tenantGuid</c> — the same text
        /// for every captured value — so <c>x =&gt; x.TenantGuid == tenant</c> produced <b>one key for all
        /// tenants</b> and the first tenant's rows were served to every other. Measured 2026-09-09: two
        /// different <c>Guid</c>s render byte-identically, while inline literals render distinctly.
        /// </para>
        /// <para>
        /// <b>Two steps, because normalisation alone is not enough.</b> First the expression is funcletized
        /// by <see cref="Birko.Data.Expressions.ExpressionNormalizer"/> — the framework's existing
        /// producer for this, reused rather than reimplemented — which folds every parameter-free subtree
        /// into a <see cref="ConstantExpression"/>, so a captured scalar becomes its value. Then the
        /// rendering is <b>checked</b>, because funcletization does not make every constant
        /// value-distinguishing: measured, <c>List&lt;int&gt;{1,2,3}</c> and <c>{9,9,9}</c> both render
        /// <c>value(System.Collections.Generic.List`1[System.Int32])</c>, so a set-membership filter such
        /// as <c>ids.Contains(x.Id)</c> would still collide across different id sets. Any surviving
        /// <c>value(</c> marker therefore means "this rendering does not distinguish the query".
        /// </para>
        /// <para>
        /// ⚠ <b>The answer there is to refuse, not to guess.</b> Returning false costs a cache miss —
        /// the query runs against the database, which is always correct. Keying two different queries the
        /// same costs one caller another caller's rows, which is the defect. So the trade is deliberate
        /// and one-directional: <b>set-membership and object-valued filters are not cached at all</b>.
        /// </para>
        /// </remarks>
        public static bool TryDescribeFilter(LambdaExpression? filter, out string? description)
        {
            description = null;
            if (filter is null)
            {
                // No filter is a perfectly distinguishable query: "everything".
                return true;
            }

            string? rendered;
            try
            {
                var normalized = Birko.Data.Expressions.ExpressionNormalizer.Normalize(filter);
                rendered = (normalized ?? filter).ToString();
            }
            catch
            {
                // DEFENSIVE, not witnessed (§ TASK-261): no throw was observed here. Normalization
                // compiles parameter-free subtrees, and ExpressionNormalizer.TryFold already swallows a
                // getter that throws — leaving the node unfolded, which the marker check below then
                // refuses, so the common case is handled by construction rather than by this catch.
                // It exists because a caching concern must never be able to fail a READ: whatever else
                // goes wrong here, "cannot be described" is the safe answer, and the caller then reads
                // straight from the database.
                return false;
            }

            if (string.IsNullOrEmpty(rendered) || rendered!.Contains(OpaqueValueMarker, StringComparison.Ordinal))
            {
                // Cannot be keyed: some subtree rendered by type/reference rather than by value.
                return false;
            }

            description = rendered;
            return true;
        }

        /// <summary>
        /// Keeps the scope segment from introducing extra <c>':'</c> separators, which would make the key
        /// ambiguous with respect to the table segment. <c>Settings.GetId()</c> is colon-delimited
        /// (<c>Location:Name:UserName:Port</c>), so this is reached on every real key rather than being
        /// defensive.
        /// </summary>
        private static string Sanitize(string? scope)
        {
            if (string.IsNullOrEmpty(scope))
            {
                return "_";
            }

            return ComputeHash(scope!);
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            // Use first 8 bytes (16 hex chars) for a compact but collision-resistant key
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
