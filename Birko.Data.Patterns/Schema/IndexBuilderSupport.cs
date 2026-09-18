using System;

namespace Birko.Data.Patterns.Schema
{
    /// <summary>
    /// One producer for the refusals an <see cref="IIndexBuilder"/> owes its caller when it cannot honour
    /// part of a declaration (TASK-274).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> Every method on <see cref="IIndexBuilder"/> returns <c>this</c>, so a builder
    /// that cannot express something has a silent option available at every step — and six backends took it.
    /// Measured 2026-08-23: <c>Sparse()</c> was <c>=> this</c> in all six implementations, so a migration
    /// asking for a sparse index got a full one; <c>WithProperty()</c> was <c>=> this</c> in all six, while
    /// the ElasticSearch and RavenDB <i>index managers</i> genuinely honour
    /// <c>IndexDefinition.Properties</c> — so the two doors onto the same feature disagreed. Worse, the
    /// ElasticSearch, RavenDB and CosmosDB builders had no <c>Build()</c> override at all, inheriting the
    /// interface's no-op default: they accumulated fields and a <c>Unique()</c> flag, held a live client, and
    /// created nothing. That is the same lost-flag defect TASK-246 fixed in the SQL builder, three backends
    /// over and total rather than partial.
    /// </para>
    /// <para>
    /// The rule this class enforces is § SH-H037's: <b>a builder honours a declaration or refuses it — it
    /// never accepts one and does nothing.</b> The refusal names what could not be honoured, why, and the
    /// door that does work, because a guard whose message only says "no" gets reached around.
    /// </para>
    /// <para>
    /// Refusing was affordable because nothing called these: measured <b>0</b> uses of <c>.Sparse()</c> and
    /// <c>0</c> of <c>.WithProperty(</c> across the framework, its tests and all 16 consumer repos. Where a
    /// backend later grows the capability, it stops calling these and honours the value instead.
    /// </para>
    /// </remarks>
    public static class IndexBuilderSupport
    {
        /// <summary>
        /// Refuses a declaration this backend cannot express at all.
        /// </summary>
        /// <param name="backend">The backend name, as a caller would recognise it (e.g. "ElasticSearch").</param>
        /// <param name="what">What was declared, in the caller's vocabulary (e.g. "a sparse index").</param>
        /// <param name="why">Why it cannot be honoured — the backend's own reason, not a paraphrase.</param>
        /// <param name="instead">What the caller can do instead. Required: a refusal without a door is a wall.</param>
        public static NotSupportedException Unsupported(string backend, string what, string why, string instead)
            => new NotSupportedException(
                $"{backend}: {what} cannot be honoured here — {why}. {instead}");

        /// <summary>
        /// Refuses a whole index declaration on a backend whose migration builder creates nothing.
        /// </summary>
        /// <remarks>
        /// Used where the builder would otherwise accumulate state and discard it. The alternative — leaving
        /// the inherited no-op <c>Build()</c> — is the defect: a migration reads as though it declared an
        /// index and the database never gets one.
        /// </remarks>
        public static NotSupportedException NotImplementedHere(string backend, string indexName, string instead)
            => new NotSupportedException(
                $"{backend}: index '{indexName}' was declared through a migration, but this backend's schema "
                + $"builder does not create indexes — it would silently do nothing. {instead}");
    }
}
