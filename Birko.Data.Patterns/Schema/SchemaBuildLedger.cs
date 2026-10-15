using System.Collections.Generic;
using Birko.Data.Patterns.IndexManagement;

namespace Birko.Data.Patterns.Schema
{
    /// <summary>
    /// The chains a schema builder handed out, so a runner can tell which were never finished with <c>Build()</c>.
    /// </summary>
    /// <remarks>
    /// TASK-627 (SH-M245): every provider defers an index to <c>Build()</c>, and SQL defers a table too, so
    /// <c>CreateIndex("Invoice", "IX_Number").WithField("Number")</c> without it emitted nothing, returned normally, and
    /// the runner recorded the migration as applied. The omission compiles whatever <c>Build()</c>'s declaration says
    /// (a caller that never calls a method is not a compile error), so it is caught when the migration ends: a builder
    /// <see cref="Track(IIndexBuilder, string, string)"/>ed here and never built is <see cref="Pending"/>, and the runner
    /// fails the migration before recording it. The rule is uniform: a collection chain is pending until built on a
    /// provider that creates the collection eagerly too, so a migration that passes on one provider passes on all.
    /// </remarks>
    public sealed class SchemaBuildLedger
    {
        private readonly object _gate = new();
        private readonly List<Entry> _entries = new();

        private sealed class Entry
        {
            public Entry(string description) => Description = description;
            public string Description { get; }
            public bool Built { get; set; }
        }

        /// <summary>The chains handed out and never finished with <c>Build()</c>, described for an error message.</summary>
        public IReadOnlyList<string> Pending
        {
            get
            {
                lock (_gate)
                {
                    var pending = new List<string>();
                    foreach (var entry in _entries)
                    {
                        if (!entry.Built) pending.Add(entry.Description);
                    }
                    return pending;
                }
            }
        }

        /// <summary>Tracks an index chain; the returned builder is the one to hand to the migration.</summary>
        public IIndexBuilder Track(IIndexBuilder inner, string collectionName, string indexName)
            => new TrackedIndexBuilder(inner, Open($"CreateIndex(\"{collectionName}\", \"{indexName}\")"), this);

        /// <summary>Tracks a collection chain; the returned builder is the one to hand to the migration.</summary>
        public ICollectionBuilder Track(ICollectionBuilder inner, string collectionName)
            => new TrackedCollectionBuilder(inner, Open($"CreateCollection(\"{collectionName}\")"), this);

        private Entry Open(string description)
        {
            var entry = new Entry(description);
            lock (_gate) _entries.Add(entry);
            return entry;
        }

        private void MarkBuilt(Entry entry)
        {
            lock (_gate) entry.Built = true;
        }

        private sealed class TrackedIndexBuilder : IIndexBuilder
        {
            private readonly IIndexBuilder _inner;
            private readonly Entry _entry;
            private readonly SchemaBuildLedger _ledger;

            public TrackedIndexBuilder(IIndexBuilder inner, Entry entry, SchemaBuildLedger ledger)
            {
                _inner = inner;
                _entry = entry;
                _ledger = ledger;
            }

            public IIndexBuilder WithField(string name, bool descending = false, IndexFieldType fieldType = IndexFieldType.Standard)
            {
                _inner.WithField(name, descending, fieldType);
                return this;
            }

            public IIndexBuilder Unique()
            {
                _inner.Unique();
                return this;
            }

            public IIndexBuilder Sparse()
            {
                _inner.Sparse();
                return this;
            }

            public IIndexBuilder WithProperty(string key, object value)
            {
                _inner.WithProperty(key, value);
                return this;
            }

            // Marked before the inner Build runs: the chain was finished. A Build that throws fails the migration
            // through its own exception.
            public void Build()
            {
                _ledger.MarkBuilt(_entry);
                _inner.Build();
            }
        }

        private sealed class TrackedCollectionBuilder : ICollectionBuilder
        {
            private readonly ICollectionBuilder _inner;
            private readonly Entry _entry;
            private readonly SchemaBuildLedger _ledger;

            public TrackedCollectionBuilder(ICollectionBuilder inner, Entry entry, SchemaBuildLedger ledger)
            {
                _inner = inner;
                _entry = entry;
                _ledger = ledger;
            }

            public ICollectionBuilder WithField(string name, FieldType type,
                bool isPrimary = false, bool isUnique = false,
                bool isRequired = false, int? maxLength = null,
                int? precision = null, int? scale = null,
                bool isAutoIncrement = false, object? defaultValue = null)
            {
                _inner.WithField(name, type, isPrimary, isUnique, isRequired, maxLength, precision, scale, isAutoIncrement, defaultValue);
                return this;
            }

            public ICollectionBuilder WithField(FieldDescriptor field)
            {
                _inner.WithField(field);
                return this;
            }

            public void Build()
            {
                _ledger.MarkBuilt(_entry);
                _inner.Build();
            }
        }
    }
}
