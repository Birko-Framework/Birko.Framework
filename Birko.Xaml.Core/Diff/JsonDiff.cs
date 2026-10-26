using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Birko.Xaml.Core.Diff;

/// <summary>
/// Structural diff of two JSON values — a port of <c>diffJson</c> in Birko.Web.Core's <c>json-diff.ts</c>, the engine
/// of record. Both are run against the shared vectors in <c>Birko.Web.Core/src/diff/diff-vectors.json</c>, so they
/// give the same paths, change kinds, statuses, counts and order.
/// </summary>
/// <remarks>
/// Two JavaScript behaviours are reproduced on purpose, because they are observable: object keys are visited in
/// JavaScript's own-key order (integer-like keys first, ascending, then insertion order), and numbers compare as
/// doubles, so <c>1</c> equals <c>1.0</c>.
/// </remarks>
public static class JsonDiff
{
    /// <summary>
    /// Diffs two JSON values. Side A is the "old" one (<see cref="JsonChange.OldValue"/>), side B the "new" one.
    /// Throws <see cref="FormatException"/> when an option's pattern is malformed, before comparing anything.
    /// </summary>
    public static JsonDiffResult Diff(JsonNode? a, JsonNode? b, JsonDiffOptions? options = null)
    {
        var run = new Run(options ?? new JsonDiffOptions());
        var tree = run.Compare(a, b, [], []);
        var changes = new List<JsonChange>();
        Collect(tree, changes);
        return new JsonDiffResult { Equal = tree.Summary.Total == 0, Changes = changes, Tree = tree, Summary = tree.Summary };
    }

    private static void Collect(JsonDiffNode node, List<JsonChange> changes)
    {
        if (node.Status is JsonDiffStatus.Added or JsonDiffStatus.Removed or JsonDiffStatus.Changed)
        {
            if (node.Summary.Total == 0) return; // a one-sided child, already reported by its root
            changes.Add(new JsonChange
            {
                Kind = node.Status switch
                {
                    JsonDiffStatus.Added => JsonChangeKind.Added,
                    JsonDiffStatus.Removed => JsonChangeKind.Removed,
                    _ => JsonChangeKind.Changed,
                },
                Path = node.Path,
                Segments = node.Segments,
                OldPath = node.OldSegments is null ? null : JsonPath.Format(node.OldSegments),
                HasOldValue = node.HasOld,
                OldValue = node.HasOld ? node.OldValue : null,
                HasNewValue = node.HasNew,
                NewValue = node.HasNew ? node.NewValue : null,
            });
            return;
        }
        if (node.Children is null) return;
        foreach (var c in node.Children) Collect(c, changes);
    }

    private sealed class Run
    {
        private readonly IList<string> _ignore;
        private readonly IList<JsonNormalizer> _normalizers;
        private readonly IList<JsonArrayRule> _arrays;

        public Run(JsonDiffOptions options)
        {
            _ignore = options.Ignore;
            _normalizers = options.Normalize;
            _arrays = options.Arrays;
            // Validate every pattern up front so a typo fails the call, not silently matches nothing.
            foreach (var p in _ignore) JsonPath.Validate(p);
            foreach (var n in _normalizers) JsonPath.Validate(n.Path);
            foreach (var r in _arrays) JsonPath.Validate(r.Path);
        }

        private bool IsIgnored(JsonPathSegment[] segs)
        {
            foreach (var p in _ignore) if (JsonPath.Matches(p, segs)) return true;
            return false;
        }

        private JsonNode? Normalize(JsonNode? v, JsonPathSegment[] segs, string path)
        {
            var o = v;
            foreach (var n in _normalizers) if (JsonPath.Matches(n.Path, segs)) o = n.Normalize(o, path);
            return o;
        }

        private JsonArrayRule? ArrayRule(JsonPathSegment[] segs)
        {
            foreach (var r in _arrays) if (JsonPath.Matches(r.Path, segs)) return r;
            return null;
        }

        /// <summary>One-sided subtree (added / removed): children repeat the status, only the root counts.</summary>
        private JsonDiffNode OneSided(JsonNode? value, JsonPathSegment[] segs, JsonPathSegment[] segsA, JsonDiffStatus status, bool counted)
        {
            var removed = status == JsonDiffStatus.Removed;
            var ignored = IsIgnored(removed ? segsA : segs);
            List<JsonDiffNode>? children = null;
            if (value is JsonArray arr)
            {
                children = new List<JsonDiffNode>(arr.Count);
                for (var i = 0; i < arr.Count; i++) children.Add(OneSided(arr[i], [.. segs, i], [.. segsA, i], status, false));
            }
            else if (value is JsonObject obj)
            {
                children = new List<JsonDiffNode>(obj.Count);
                foreach (var k in OwnKeys(obj)) children.Add(OneSided(obj[k], [.. segs, k], [.. segsA, k], status, false));
            }
            var summary = counted && !ignored
                ? (removed ? new JsonDiffSummary(0, 1, 0) : new JsonDiffSummary(1, 0, 0))
                : JsonDiffSummary.Empty;
            return new JsonDiffNode
            {
                Key = segs.Length > 0 ? segs[^1] : (JsonPathSegment?)null,
                Path = JsonPath.Format(segs),
                Segments = segs,
                OldSegments = SameSegs(segs, segsA) ? null : segsA,
                Status = ignored ? JsonDiffStatus.Ignored : status,
                Type = ShapeOf(value),
                HasOld = removed,
                HasNew = !removed,
                OldValue = removed ? value : null,
                NewValue = removed ? null : value,
                Children = children,
                Summary = summary,
            };
        }

        public JsonDiffNode Compare(JsonNode? rawA, JsonNode? rawB, JsonPathSegment[] segs, JsonPathSegment[] segsA)
        {
            var path = JsonPath.Format(segs);
            JsonDiffNode Node(JsonDiffStatus status, JsonNodeType type, JsonDiffSummary summary, IReadOnlyList<JsonDiffNode>? children = null) => new()
            {
                Key = segs.Length > 0 ? segs[^1] : (JsonPathSegment?)null,
                Path = path,
                Segments = segs,
                OldSegments = SameSegs(segs, segsA) ? null : segsA,
                Status = status,
                Type = type,
                HasOld = true,
                HasNew = true,
                OldValue = rawA,
                NewValue = rawB,
                Children = children,
                Summary = summary,
            };

            if (IsIgnored(segs)) return Node(JsonDiffStatus.Ignored, ShapeOf(rawB), JsonDiffSummary.Empty);
            var na = Normalize(rawA, segs, path);
            var nb = Normalize(rawB, segs, path);
            var ta = ShapeOf(na);
            var tb = ShapeOf(nb);

            if (ta != tb || ta == JsonNodeType.Value)
            {
                var same = ta == tb && ValueEquals(na, nb);
                return Node(same ? JsonDiffStatus.Equal : JsonDiffStatus.Changed, tb, same ? JsonDiffSummary.Empty : new JsonDiffSummary(0, 0, 1));
            }

            var children = ta == JsonNodeType.Object
                ? CompareObjects((JsonObject)na!, (JsonObject)nb!, segs, segsA)
                : CompareArrays((JsonArray)na!, (JsonArray)nb!, segs, segsA);
            var total = JsonDiffSummary.Empty;
            foreach (var c in children) total += c.Summary;
            return Node(total.Total > 0 ? JsonDiffStatus.Modified : JsonDiffStatus.Equal, tb, total, children);
        }

        private List<JsonDiffNode> CompareObjects(JsonObject a, JsonObject b, JsonPathSegment[] segs, JsonPathSegment[] segsA)
        {
            var o = new List<JsonDiffNode>();
            foreach (var k in OwnKeys(b))
            {
                o.Add(a.ContainsKey(k)
                    ? Compare(a[k], b[k], [.. segs, k], [.. segsA, k])
                    : OneSided(b[k], [.. segs, k], [.. segsA, k], JsonDiffStatus.Added, true));
            }
            foreach (var k in OwnKeys(a))
                if (!b.ContainsKey(k)) o.Add(OneSided(a[k], [.. segs, k], [.. segsA, k], JsonDiffStatus.Removed, true));
            return o;
        }

        private List<JsonDiffNode> CompareArrays(JsonArray a, JsonArray b, JsonPathSegment[] segs, JsonPathSegment[] segsA)
        {
            var rule = ArrayRule(segs);
            if (rule is null || (rule.Key is null && rule.KeySelector is null && rule.Match == JsonArrayMatch.Index))
            {
                var byIndex = new List<JsonDiffNode>();
                var n = Math.Max(a.Count, b.Count);
                for (var i = 0; i < n; i++)
                {
                    if (i >= a.Count) byIndex.Add(OneSided(b[i], [.. segs, i], [.. segsA, i], JsonDiffStatus.Added, true));
                    else if (i >= b.Count) byIndex.Add(OneSided(a[i], [.. segs, i], [.. segsA, i], JsonDiffStatus.Removed, true));
                    else byIndex.Add(Compare(a[i], b[i], [.. segs, i], [.. segsA, i]));
                }
                return byIndex;
            }

            // Paired modes. A pair is reported at its side-B index, an unpaired A item at its side-A index.
            var pairOf = Enumerable.Repeat(-1, b.Count).ToArray();
            var usedA = new HashSet<int>();
            Func<JsonNode?, object?>? keyOf = rule.KeySelector is { } selector
                ? item => PairKey.From(selector(item))
                : rule.Key is { } property
                    ? item => item is JsonObject o && o.TryGetPropertyValue(property, out var v) ? PairKey.FromNode(v) : null
                    : null;
            if (keyOf is not null)
            {
                var byKey = new Dictionary<object, Queue<int>>();
                for (var i = 0; i < a.Count; i++)
                {
                    var k = keyOf(a[i]);
                    if (k is null) continue;
                    if (!byKey.TryGetValue(k, out var list)) byKey[k] = list = new Queue<int>();
                    list.Enqueue(i);
                }
                for (var j = 0; j < b.Count; j++)
                {
                    var k = keyOf(b[j]);
                    if (k is not null && byKey.TryGetValue(k, out var list) && list.Count > 0)
                    {
                        var i = list.Dequeue();
                        pairOf[j] = i;
                        usedA.Add(i);
                    }
                }
            }
            // Unordered pass: everything still unpaired (all of it, or the keyless items) pairs with an equal item.
            for (var j = 0; j < b.Count; j++)
            {
                if (pairOf[j] >= 0 || (keyOf is not null && keyOf(b[j]) is not null)) continue;
                for (var i = 0; i < a.Count; i++)
                {
                    if (usedA.Contains(i) || (keyOf is not null && keyOf(a[i]) is not null)) continue;
                    if (Compare(a[i], b[j], [.. segs, j], [.. segsA, i]).Summary.Total == 0)
                    {
                        pairOf[j] = i;
                        usedA.Add(i);
                        break;
                    }
                }
            }

            var paired = new List<JsonDiffNode>(b.Count);
            for (var j = 0; j < b.Count; j++)
            {
                paired.Add(pairOf[j] >= 0
                    ? Compare(a[pairOf[j]], b[j], [.. segs, j], [.. segsA, pairOf[j]])
                    : OneSided(b[j], [.. segs, j], [.. segsA, j], JsonDiffStatus.Added, true));
            }
            // A removed item is listed after the pairs under its side-A index, so its path can repeat a pair's.
            for (var i = 0; i < a.Count; i++)
                if (!usedA.Contains(i)) paired.Add(OneSided(a[i], [.. segs, i], [.. segsA, i], JsonDiffStatus.Removed, true));
            return paired;
        }
    }

    private static JsonNodeType ShapeOf(JsonNode? v) => v switch
    {
        JsonArray => JsonNodeType.Array,
        JsonObject => JsonNodeType.Object,
        _ => JsonNodeType.Value,
    };

    private static bool SameSegs(JsonPathSegment[] x, JsonPathSegment[] y)
    {
        if (x.Length != y.Length) return false;
        for (var i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
        return true;
    }

    /// <summary>
    /// JavaScript's own-key order for an object parsed from JSON: keys that are canonical array indices (up to
    /// 2^32 − 2) first, in ascending numeric order, then every other key in insertion order.
    /// </summary>
    public static IEnumerable<string> OwnKeys(JsonObject obj)
    {
        List<(uint Index, string Key)>? indexKeys = null;
        List<string>? rest = null;
        foreach (var (k, _) in obj)
        {
            if (IsArrayIndex(k, out var idx)) (indexKeys ??= new()).Add((idx, k));
            else (rest ??= new()).Add(k);
        }
        if (indexKeys is null) return rest ?? (IEnumerable<string>)Array.Empty<string>();
        indexKeys.Sort((p, q) => p.Index.CompareTo(q.Index));
        var ordered = indexKeys.Select(p => p.Key);
        return rest is null ? ordered : ordered.Concat(rest);
    }

    private static bool IsArrayIndex(string k, out uint index)
    {
        index = 0;
        if (k.Length == 0 || k.Length > 10) return false;
        if (k[0] == '0') return k.Length == 1;
        foreach (var c in k) if (!char.IsAsciiDigit(c)) return false;
        return uint.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index != uint.MaxValue;
    }

    /// <summary>JavaScript <c>===</c> on two JSON values: numbers as doubles, strings ordinal, and null equals null.</summary>
    private static bool ValueEquals(JsonNode? x, JsonNode? y)
    {
        if (x is null || y is null) return x is null && y is null;
        if (x is not JsonValue vx || y is not JsonValue vy) return ReferenceEquals(x, y);
        var kx = vx.GetValueKind();
        var ky = vy.GetValueKind();
        if (kx == JsonValueKind.Number && ky == JsonValueKind.Number) return ToDouble(vx) == ToDouble(vy);
        if (kx != ky) return false;
        return kx switch
        {
            JsonValueKind.String => vx.GetValue<string>() == vy.GetValue<string>(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => vx.ToJsonString() == vy.ToJsonString(),
        };
    }

    internal static double ToDouble(JsonValue v) =>
        v.TryGetValue<double>(out var d) ? d : double.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// A pairing key with JavaScript <c>Map</c> semantics (SameValueZero): numbers by value, strings and booleans by
    /// value, JSON null as a key of its own, objects and arrays by reference. <c>null</c> means "no key".
    /// </summary>
    private static class PairKey
    {
        private static readonly object NullKey = new();

        public static object? FromNode(JsonNode? v)
        {
            if (v is null) return NullKey;
            if (v is not JsonValue value) return v; // by reference, as a JS object key
            return value.GetValueKind() switch
            {
                JsonValueKind.Number => (object)ToDouble(value),
                JsonValueKind.String => ("s", value.GetValue<string>()),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => NullKey,
                _ => value.ToJsonString(),
            };
        }

        public static object? From(object? v) => v switch
        {
            null => null,
            JsonNode node => FromNode(node),
            string s => ("s", s),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                Convert.ToDouble(v, CultureInfo.InvariantCulture),
            _ => v,
        };
    }
}
