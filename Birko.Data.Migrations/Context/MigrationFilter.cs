using System;
using Birko.Data.Exceptions;

namespace Birko.Data.Migrations.Context
{
    /// <summary>
    /// The single producer for "does this migration filter actually constrain anything?" (TASK-314,
    /// <c>SH-H032</c>).
    ///
    /// <para>
    /// <b>Why this exists.</b> Every <see cref="IDataMigrator"/> that translates the Mongo-style JSON
    /// filter — SQL, ElasticSearch, RavenDB, CosmosDB — returned an <i>empty</i> translation for two
    /// unrelated inputs, and its caller appended the constraint only when the translation was non-empty:
    /// <list type="bullet">
    /// <item>no filter at all (<c>null</c>, whitespace, <c>"{}"</c>) — a deliberate match-all; and</item>
    /// <item>a filter that named fields but yielded no terms, e.g. <c>{"status":{}}</c>, where the object
    /// branch is taken and the operator loop adds nothing.</item>
    /// </list>
    /// The second is a typo, and it rendered <c>DELETE FROM {table}</c> with no <c>WHERE</c> — the whole
    /// collection deleted, or every row rewritten, reported as success.
    /// </para>
    ///
    /// <para>
    /// <b>Guard on the rendered result, never re-parse.</b> <see cref="RequireBounded"/> takes the
    /// <i>outcome</i> of the backend's own translator rather than inspecting the JSON a second time, so the
    /// guard and the emitted statement cannot disagree about what "constrains nothing" means. That is the
    /// § TASK-137 rule ("one producer for the scope decision"); a guard that counts terms independently is
    /// how it ends up agreeing with itself and disagreeing with the query.
    /// </para>
    ///
    /// <para>
    /// <b>It refuses reads too.</b> A <c>CountDocuments</c> over the same degraded filter answers with the
    /// whole collection's count, which is a silently wrong number produced by the identical mistake. Guarding
    /// the write and not the read would leave a destructive statement disagreeing with its own preview
    /// (§ TASK-215, § TASK-313). Refusing cannot break working code here: the only way to mean
    /// "everything" in this dialect is the explicit door, which stays untouched.
    /// </para>
    /// </summary>
    public static class MigrationFilter
    {
        /// <summary>
        /// The deliberate match-everything door: no filter supplied at all. Every migrator already spelled
        /// this test inline and identically; it is stated once here so the guard and the callers cannot
        /// drift about which inputs are intentional.
        /// </summary>
        public static bool IsExplicitMatchAll(string? filterJson)
            => string.IsNullOrWhiteSpace(filterJson) || filterJson!.Trim() == "{}";

        /// <summary>
        /// Refuses when a filter was supplied yet the backend's translation of it constrains nothing.
        /// </summary>
        /// <param name="filterJson">The caller's filter, exactly as handed to the migrator.</param>
        /// <param name="translationConstrains">
        /// What the backend's own translator produced: <c>true</c> when it yielded at least one term. Pass
        /// the rendered result (a non-empty clause, a non-empty must-list) — not a re-derivation.
        /// </param>
        /// <param name="operation">The verb being refused, e.g. <c>"delete"</c>, for
        /// <see cref="WholeTableWriteException.Operation"/>.</param>
        /// <param name="collection">Table / index / collection / container name being targeted.</param>
        /// <param name="scope">What the unconstrained operation would cover, in the backend's own words.</param>
        /// <exception cref="WholeTableWriteException">The filter was supplied and yielded no terms.</exception>
        public static void RequireBounded(
            string? filterJson,
            bool translationConstrains,
            string operation,
            string collection,
            string scope)
        {
            if (translationConstrains) return;
            if (IsExplicitMatchAll(filterJson)) return;

            throw WholeTableWriteException.ForDataFilter(
                operation,
                collection,
                scope,
                "an empty filter — null or \"{}\"");
        }
    }
}
