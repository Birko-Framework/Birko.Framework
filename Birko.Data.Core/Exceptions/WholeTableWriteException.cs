using System;

namespace Birko.Data.Exceptions
{
    /// <summary>
    /// Thrown when a <c>DELETE</c> or <c>UPDATE</c> is about to be issued with **no <c>WHERE</c> clause**, so
    /// it would affect every row in the table.
    ///
    /// <para>
    /// <b>Why this exists (SH-H002).</b> The condition parser returns an empty condition set for several
    /// unrelated reasons — a null filter, a predicate that reduces to <c>true</c>, and <b>every predicate
    /// shape it cannot translate</b> (an <c>InvocationExpression</c> from <c>x =&gt; pred(x)</c>, for
    /// instance). The connector then appended the <c>WHERE</c> only when conditions existed, so all of those
    /// rendered a bare <c>DELETE FROM "T"</c>. A silently dropped filter was indistinguishable from no filter
    /// at all, and the result was a whole-table write reported as success.
    /// </para>
    /// <para>
    /// <b>The causes are deliberately not distinguished.</b> At the point of decision they are the same
    /// observation — nothing rendered — and they have the same correct answer: refuse. Machinery to tell them
    /// apart would enrich the message without changing the outcome, and inferring "untranslatable" from an
    /// empty result is unsound anyway: several predicates legitimately mean "every row" and correctly render
    /// nothing.
    /// </para>
    /// <para>
    /// <b>Affecting every row on purpose is still possible</b>, it just has to be said out loud:
    /// <c>DeleteAll()</c> / <c>UpdateAll(updates)</c>, or the equivalent <c>x =&gt; true</c> predicate. Those
    /// reach the same conditionless statement through an explicit door, so a bare <c>DELETE FROM "T"</c> in a
    /// query log now means somebody asked for it. Use <c>Destroy()</c> to drop the table instead of emptying
    /// it.
    /// </para>
    /// <para>
    /// Derives from <see cref="InvalidOperationException"/> on purpose, mirroring
    /// <c>TenantScopeRequiredException</c>: existing <c>catch (InvalidOperationException)</c> blocks keep
    /// working, while a host that wants to report this case distinctly can catch this type first. It is a
    /// request-shaped problem (the caller asked for something unsafe), not a server fault.
    /// </para>
    /// </summary>
    public class WholeTableWriteException : InvalidOperationException
    {
        /// <summary>The refused operation — <c>delete</c> or <c>update</c>.</summary>
        public string Operation { get; }

        /// <summary>The table the statement would have targeted.</summary>
        public string TableName { get; }

        public WholeTableWriteException(string operation, string tableName)
            : base($"Refusing to {operation} in \"{tableName}\" without a WHERE clause: the statement would "
                 + "affect every row. A null filter, a predicate that reduces to `true`, and a predicate the "
                 + "translator cannot express all arrive here identically. To target every row deliberately "
                 + $"use {(operation == "delete" ? "DeleteAll()" : "UpdateAll(updates)")} or an explicit "
                 + "`x => true` filter; to drop the table use Destroy().")
        {
            Operation = operation;
            TableName = tableName;
        }

        /// <summary>
        /// The backend-neutral refusal, for a store that never builds a SQL statement (TASK-212). Same type
        /// and same <see cref="Operation"/> / <see cref="TableName"/> contract, so one
        /// <c>catch (WholeTableWriteException)</c> selects the refusal on every backend; only the wording
        /// differs, because "without a WHERE clause" and "Destroy()" describe machinery a document store does
        /// not have and would send the reader looking for an API that isn't there.
        /// </summary>
        /// <param name="scope">What the write would have covered, in the backend's own words — e.g.
        /// <c>"every document in the collection"</c>.</param>
        /// <param name="explicitDoor">The all-rows method the caller actually has, e.g.
        /// <c>"DeleteAllAsync()"</c>. Defaults to the synchronous spelling.
        /// <para><b>TASK-215, § SH-H037.</b> This parameter exists because the default was wrong for half its
        /// callers: an async store's caller has <c>DeleteAllAsync()</c>, and a refusal telling them to use
        /// <c>DeleteAll()</c> points at a method that does not compile. The rule is that a guard's opt-out
        /// must be one the reader can actually take — a message naming an unreachable door is a wall wearing
        /// a door's label, the same defect the Redis flush guard shipped in louder form.</para></param>
        public WholeTableWriteException(string operation, string tableName, string scope, string? explicitDoor = null)
            : base($"Refusing to {operation} in \"{tableName}\": the filter constrains nothing, so the "
                 + $"operation would affect {scope}. "
                 + "A predicate that reduces to `true` is indistinguishable, once translated, from an "
                 + "ordinary one. To target everything deliberately use "
                 + $"{explicitDoor ?? (operation == "delete" ? "DeleteAll()" : "UpdateAll(updates)")} or an explicit "
                 + "`x => true` filter.")
        {
            Operation = operation;
            TableName = tableName;
        }
    }
}
