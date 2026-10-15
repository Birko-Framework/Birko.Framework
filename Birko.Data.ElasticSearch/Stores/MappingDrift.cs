using System;
using System.Collections.Generic;
using System.Linq;
using Nest;

namespace Birko.Data.ElasticSearch.Stores
{
    /// <summary>
    /// How a stored index field differs from what the store's index descriptor declares.
    /// </summary>
    public enum MappingDriftKind
    {
        /// <summary>Declared, absent from the index. Queries on it match nothing.</summary>
        Missing = 0,

        /// <summary>
        /// In the index, not declared: a removed property, a dynamic sub-field an older model got (<c>code.keyword</c>),
        /// or a field another writer owns. Reported, never actioned.
        /// </summary>
        Unexpected = 1,

        /// <summary>
        /// Both have the field with different types, e.g. <c>keyword</c> declared over a stored <c>text</c>. The query
        /// side is translated for the declared type, so this is where silently empty results come from (TASK-613).
        /// </summary>
        TypeMismatch = 2,
    }

    /// <summary>One field's disagreement between a store's declared mapping and its index (TASK-621).</summary>
    public sealed class MappingDrift
    {
        public MappingDrift(string index, string field, MappingDriftKind kind, string? declared, string? stored)
        {
            Index = index;
            Field = field;
            Kind = kind;
            Declared = declared;
            Stored = stored;
        }

        public string Index { get; }

        /// <summary>The dotted field path, sub-fields included (<c>code.keyword</c>).</summary>
        public string Field { get; }

        public MappingDriftKind Kind { get; }

        /// <summary>The declared field type, or null for <see cref="MappingDriftKind.Unexpected"/>.</summary>
        public string? Declared { get; }

        /// <summary>The stored field type, or null for <see cref="MappingDriftKind.Missing"/>.</summary>
        public string? Stored { get; }

        public override string ToString() => Kind switch
        {
            MappingDriftKind.Missing => $"{Index}.{Field}: declared {Declared}, not mapped",
            MappingDriftKind.Unexpected => $"{Index}.{Field}: mapped as {Stored}, not declared",
            _ => $"{Index}.{Field}: declared {Declared}, mapped as {Stored}",
        };
    }

    /// <summary>
    /// The outcome of comparing a store's declared mapping with its index. Shaped like the SQL
    /// <c>SchemaDriftReport</c>; it cannot be that type, because this project may not reference the SQL ones.
    /// </summary>
    /// <remarks>
    /// ⚠ <see cref="Supported"/> and <see cref="IndexExists"/> false are not "no drift": a question that was not answered
    /// is never a clean bill of health, so <see cref="IsClean"/> needs both.
    /// </remarks>
    public sealed class MappingDriftReport
    {
        public MappingDriftReport(string index, bool supported, bool indexExists, IReadOnlyList<MappingDrift> drifts, string? reason = null)
        {
            Index = index;
            Supported = supported;
            IndexExists = indexExists;
            Drifts = drifts;
            Reason = reason;
        }

        public string Index { get; }

        /// <summary>False when the store has no client to ask.</summary>
        public bool Supported { get; }

        /// <summary>False when the index does not exist yet: a store creates it on first use, so this is not drift.</summary>
        public bool IndexExists { get; }

        public IReadOnlyList<MappingDrift> Drifts { get; }

        /// <summary>Why the report is unsupported or empty, when that needs saying.</summary>
        public string? Reason { get; }

        public bool IsClean => Supported && IndexExists && Drifts.Count == 0;
    }

    /// <summary>
    /// Compares a declared mapping with a stored one (TASK-621). Both are flattened to dotted paths, object children and
    /// sub-fields included, and compared by field type.
    /// </summary>
    internal static class ElasticMappingDrift
    {
        /// <summary>
        /// The mapping <paramref name="descriptor"/> would create, which is the declared side: the store builds its index
        /// from the same descriptor, so the two cannot disagree (rule 63).
        /// </summary>
        public static ITypeMapping? Declared(Func<CreateIndexDescriptor, ICreateIndexRequest> descriptor, string index)
            => descriptor(new CreateIndexDescriptor(index)).Mappings;

        public static MappingDriftReport Compare(string index, ITypeMapping? declared, ITypeMapping? stored, Inferrer infer)
        {
            var want = Flatten(declared?.Properties, infer);
            var have = Flatten(stored?.Properties, infer);
            var drifts = new List<MappingDrift>();

            foreach (var kvp in want)
            {
                if (!have.TryGetValue(kvp.Key, out var storedType))
                {
                    drifts.Add(new MappingDrift(index, kvp.Key, MappingDriftKind.Missing, kvp.Value, null));
                }
                else if (!string.Equals(kvp.Value, storedType, StringComparison.Ordinal))
                {
                    drifts.Add(new MappingDrift(index, kvp.Key, MappingDriftKind.TypeMismatch, kvp.Value, storedType));
                }
            }
            foreach (var kvp in have.Where(h => !want.ContainsKey(h.Key)))
            {
                drifts.Add(new MappingDrift(index, kvp.Key, MappingDriftKind.Unexpected, null, kvp.Value));
            }

            return new MappingDriftReport(index, supported: true, indexExists: true, drifts);
        }

        private static Dictionary<string, string> Flatten(IProperties? properties, Inferrer infer)
        {
            // Field names are case-sensitive in Elasticsearch, so the paths are compared exactly.
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(properties, infer, null, result);
            return result;
        }

        private static void Flatten(IProperties? properties, Inferrer infer, string? prefix, Dictionary<string, string> into)
        {
            if (properties == null) return;
            foreach (var kvp in properties)
            {
                var name = infer.PropertyName(kvp.Key) ?? kvp.Value.Name?.Name;
                if (string.IsNullOrEmpty(name)) continue;
                var path = prefix == null ? name! : $"{prefix}.{name}";
                var property = kvp.Value;

                // `AutoMap().Properties(...)` keeps both entries for an overridden property, and Elasticsearch applies
                // the last. So an overwrite replaces the whole earlier sub-tree, or a stale `name.keyword` survives it.
                foreach (var stale in into.Keys.Where(k => k.StartsWith(path + ".", StringComparison.Ordinal)).ToList())
                {
                    into.Remove(stale);
                }

                // An object has no "type" in a stored mapping; give both sides the same spelling.
                into[path] = string.IsNullOrEmpty(property.Type) ? "object" : property.Type;

                if (property is IObjectProperty obj)
                {
                    Flatten(obj.Properties, infer, path, into);
                }
                if (property is ICoreProperty core)
                {
                    Flatten(core.Fields, infer, path, into);
                }
            }
        }
    }
}
