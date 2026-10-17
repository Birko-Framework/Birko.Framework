using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.IndexManagement
{
    /// <summary>How a declared index disagrees with the stored one (the kinds SQL schema drift reports, TASK-622).</summary>
    public enum IndexDeclarationDriftKind
    {
        /// <summary>Declared, not present. Queries that rely on it scan, and a declared unique rule is unenforced.</summary>
        Missing = 0,

        /// <summary>Present under that name, over different fields, in a different order or direction.</summary>
        ColumnsDiffer = 1,

        /// <summary>Declared unique and stored plain, or the reverse. A plain one admits the duplicates the model forbids.</summary>
        UniquenessDiffers = 2,

        /// <summary>
        /// Declared partial and stored full, or over other predicates (TASK-636). A full unique index refuses rows the
        /// declaration allows; a partial one where a full one was declared admits the duplicates it forbids.
        /// </summary>
        PredicateDiffers = 3,
    }

    /// <summary>One declared index's disagreement with the store (TASK-635).</summary>
    public sealed class IndexDeclarationDrift
    {
        public IndexDeclarationDrift(string? scope, string index, IndexDeclarationDriftKind kind, string declared, string? stored)
        {
            Scope = scope;
            Index = index;
            Kind = kind;
            Declared = declared;
            Stored = stored;
        }

        /// <summary>The collection, container or index-manager scope the index was looked for in.</summary>
        public string? Scope { get; }

        /// <summary>The index name as declared.</summary>
        public string Index { get; }

        public IndexDeclarationDriftKind Kind { get; }

        /// <summary>The declared shape, e.g. <c>UNIQUE (TenantGuid, Number DESC)</c>.</summary>
        public string Declared { get; }

        /// <summary>The stored shape, or null for <see cref="IndexDeclarationDriftKind.Missing"/>.</summary>
        public string? Stored { get; }

        public override string ToString() => Kind == IndexDeclarationDriftKind.Missing
            ? $"{Scope}.{Index}: declared {Declared}, not present"
            : $"{Scope}.{Index}: {Kind}, declared {Declared}, stored {Stored}";
    }

    /// <summary>A declared index the detector could not compare, and why.</summary>
    public sealed class IndexDeclarationUnverified
    {
        public IndexDeclarationUnverified(string? scope, string index, string reason)
        {
            Scope = scope;
            Index = index;
            Reason = reason;
        }

        public string? Scope { get; }
        public string Index { get; }
        public string Reason { get; }

        public override string ToString() => $"{Scope}.{Index}: not compared ({Reason})";
    }

    /// <summary>The outcome of comparing declared indexes with one scope of an <see cref="IIndexManager"/>.</summary>
    public sealed class IndexDeclarationReport
    {
        public IndexDeclarationReport(string? scope, int declared, IReadOnlyList<IndexDeclarationDrift> drifts, IReadOnlyList<IndexDeclarationUnverified> unverified)
        {
            Scope = scope;
            Declared = declared;
            Drifts = drifts;
            Unverified = unverified;
        }

        public string? Scope { get; }

        /// <summary>How many indexes were declared.</summary>
        public int Declared { get; }

        public IReadOnlyList<IndexDeclarationDrift> Drifts { get; }

        /// <summary>Declared indexes whose stored side could not be read or compared: never counted as clean.</summary>
        public IReadOnlyList<IndexDeclarationUnverified> Unverified { get; }

        /// <summary>True when every declared index was compared and none drifted.</summary>
        public bool IsClean => Drifts.Count == 0 && Unverified.Count == 0;
    }

    /// <summary>
    /// Compares declared <see cref="IndexDefinition"/>s with what an <see cref="IIndexManager"/> reports (TASK-635).
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQL derives its declared indexes from the model (<c>[IndexedField]</c>, fluent keys) and checks them itself
    /// (<c>DetectDrift</c>, TASK-622). The document stores have no model-level index declaration: an index on MongoDB,
    /// RavenDB or Cosmos DB exists because a host or a migration created it from an <see cref="IndexDefinition"/>. So the
    /// declared side here is that same list, the one a host passes to <see cref="IIndexManager.CreateAsync"/>, and the
    /// stored side is the manager's own <see cref="IIndexManager.GetInfoAsync"/>. Detection only: nothing is created.
    /// </para>
    /// <para>
    /// <b>"Could not compare" is never clean.</b> A provider that reports no fields for an index (a RavenDB index whose map
    /// it did not generate, a Cosmos path entry nothing registered), or a lookup that throws, is listed in
    /// <see cref="IndexDeclarationReport.Unverified"/> with the reason.
    /// </para>
    /// </remarks>
    public static class IndexDeclarationDriftDetector
    {
        /// <summary>Compares <paramref name="declared"/> with the indexes <paramref name="manager"/> reports in <paramref name="scope"/>.</summary>
        public static async Task<IndexDeclarationReport> DetectAsync(
            IIndexManager manager, IEnumerable<IndexDefinition> declared, string? scope = null, CancellationToken ct = default)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (declared == null) throw new ArgumentNullException(nameof(declared));

            var definitions = declared.ToList();
            var drifts = new List<IndexDeclarationDrift>();
            var unverified = new List<IndexDeclarationUnverified>();

            foreach (var definition in definitions)
            {
                ct.ThrowIfCancellationRequested();
                if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
                    throw new ArgumentException("Every declared index needs a name.", nameof(declared));

                IndexInfo? stored;
                try
                {
                    stored = await manager.GetInfoAsync(definition.Name, scope, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    unverified.Add(new IndexDeclarationUnverified(scope, definition.Name, "the lookup failed: " + ex.Message));
                    continue;
                }

                var shape = Describe(definition.Unique, definition.Fields, definition.WhereNotNull, definition.WhereNull);
                if (stored == null)
                {
                    drifts.Add(new IndexDeclarationDrift(scope, definition.Name, IndexDeclarationDriftKind.Missing, shape, null));
                    continue;
                }

                var storedShape = Describe(stored.Unique, stored.Fields, stored.WhereNotNull, stored.WhereNull);
                if (stored.Fields.Count == 0)
                {
                    unverified.Add(new IndexDeclarationUnverified(scope, definition.Name, "the store reports no fields for it"));
                }
                else if (!SameFields(definition.Fields, stored.Fields))
                {
                    drifts.Add(new IndexDeclarationDrift(scope, definition.Name, IndexDeclarationDriftKind.ColumnsDiffer, shape, storedShape));
                }

                if (definition.Unique != stored.Unique)
                {
                    drifts.Add(new IndexDeclarationDrift(scope, definition.Name, IndexDeclarationDriftKind.UniquenessDiffers, shape, storedShape));
                }

                // TASK-636: the predicate is part of the index. A manager that does not report predicates cannot confirm a
                // declared one (never read as "none"); a predicate it could not parse is not compared either.
                if (stored.UnrecognizedPredicate != null)
                {
                    unverified.Add(new IndexDeclarationUnverified(scope, definition.Name, "the stored predicate is not WhereNotNull / WhereNull: " + stored.UnrecognizedPredicate));
                }
                else if (stored.WhereNotNull == null || stored.WhereNull == null)
                {
                    if (definition.HasPredicates)
                        unverified.Add(new IndexDeclarationUnverified(scope, definition.Name, "the store does not report index predicates"));
                }
                else if (!SameSet(definition.WhereNotNull, stored.WhereNotNull) || !SameSet(definition.WhereNull, stored.WhereNull))
                {
                    drifts.Add(new IndexDeclarationDrift(scope, definition.Name, IndexDeclarationDriftKind.PredicateDiffers, shape, storedShape));
                }
            }

            return new IndexDeclarationReport(scope, definitions.Count, drifts, unverified);
        }

        /// <remarks>
        /// TASK-651: the text fields of an index are compared as a set. A MongoDB text index stores them as one weights
        /// document, which the server returns in its own order rather than the declared one, and their order means nothing
        /// to the index. Every other field is compared in order, as the index uses it.
        /// </remarks>
        private static bool SameFields(IReadOnlyList<IndexField> declared, IReadOnlyList<IndexField> stored)
        {
            static bool IsText(IndexField f) => f.FieldType == IndexFieldType.Text;
            var declaredOrdered = declared.Where(f => !IsText(f)).ToList();
            var storedOrdered = stored.Where(f => !IsText(f)).ToList();
            return declared.Count == stored.Count
                && declaredOrdered.Count == storedOrdered.Count
                && declaredOrdered.Zip(storedOrdered, (d, s) => string.Equals(d.Name, s.Name, StringComparison.Ordinal)
                                                                && d.IsDescending == s.IsDescending
                                                                && d.FieldType == s.FieldType).All(x => x)
                && new HashSet<string>(declared.Where(IsText).Select(f => f.Name), StringComparer.Ordinal)
                    .SetEquals(stored.Where(IsText).Select(f => f.Name));
        }

        private static bool SameSet(IReadOnlyList<string> declared, IReadOnlyList<string> stored)
            => new HashSet<string>(declared, StringComparer.Ordinal).SetEquals(stored);

        internal static string Describe(bool unique, IReadOnlyList<IndexField> fields,
            IReadOnlyList<string>? whereNotNull = null, IReadOnlyList<string>? whereNull = null)
        {
            var shape = (unique ? "UNIQUE " : "") + "(" + string.Join(", ", fields.Select(f =>
                f.Name + (f.IsDescending ? " DESC" : "") + (f.FieldType != IndexFieldType.Standard ? " " + f.FieldType : ""))) + ")";
            var terms = (whereNotNull ?? Array.Empty<string>()).Select(f => f + " IS NOT NULL")
                .Concat((whereNull ?? Array.Empty<string>()).Select(f => f + " IS NULL")).ToList();
            return terms.Count == 0 ? shape : shape + " WHERE " + string.Join(" AND ", terms);
        }
    }
}
