using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Birko.Xaml.Core.Diff;

namespace Birko.Xaml.Core.Tests;

/// <summary>
/// Reads Birko.Web.Core's shared diff vectors (TASK-769) from the sibling <c>Birko\Web</c> checkout, located as
/// Birko.DesignTokens locates it: <c>BIRKO_SRC</c> (the Framework bucket) and its sibling <c>Web</c>, else a walk up
/// to the directory holding both buckets. Absent, a suite skips with a line saying so — unless
/// <c>BIRKO_REQUIRE_LIVE</c> is set, where it fails, as the live suites do.
/// </summary>
internal static class DiffVectors
{
    private const string RelativePath = "Birko.Web.Core/src/diff/diff-vectors.json";

    public static JsonObject? Load(Action<string> log)
    {
        var path = Locate();
        if (path is not null) return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException($"The Birko\\Web checkout ({RelativePath}) was not found but BIRKO_REQUIRE_LIVE is set.");
        log($"SKIPPED: {RelativePath} not found (no Birko\\Web checkout beside Birko\\Framework; set BIRKO_SRC).");
        return null;
    }

    private static string? Locate()
    {
        var birkoSrc = Environment.GetEnvironmentVariable("BIRKO_SRC");
        if (!string.IsNullOrWhiteSpace(birkoSrc))
        {
            var viaEnv = Path.GetFullPath(Path.Combine(birkoSrc, "..", "Web", RelativePath));
            if (File.Exists(viaEnv)) return viaEnv;
        }
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!Directory.Exists(Path.Combine(dir.FullName, "Framework"))) continue;
            var candidate = Path.Combine(dir.FullName, "Web", RelativePath);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The vectors' named normalizer ops, as the web runner (diff-vectors-smoke) reads them.</summary>
    public static JsonDiffOptions Options(JsonNode? node)
    {
        var options = new JsonDiffOptions();
        if (node is not JsonObject o) return options;
        if (o["ignore"] is JsonArray ignore)
            foreach (var p in ignore) options.Ignore.Add(p!.GetValue<string>());
        if (o["normalize"] is JsonArray normalize)
            foreach (var n in normalize) options.Normalize.Add(new JsonNormalizer(n!["path"]!.GetValue<string>(), Op(n.AsObject())));
        if (o["arrays"] is JsonArray arrays)
        {
            foreach (var r in arrays)
            {
                options.Arrays.Add(new JsonArrayRule(r!["path"]!.GetValue<string>())
                {
                    Match = r["match"]?.GetValue<string>() == "unordered" ? JsonArrayMatch.Unordered : JsonArrayMatch.Index,
                    Key = r["key"]?.GetValue<string>(),
                });
            }
        }
        return options;
    }

    private static Func<JsonNode?, string, JsonNode?> Op(JsonObject n)
    {
        var op = n["op"]!.GetValue<string>();
        string? Str(JsonNode? v) => v is JsonValue s && s.GetValueKind() == JsonValueKind.String ? s.GetValue<string>() : null;
        switch (op)
        {
            case "trim": return (v, _) => Str(v) is { } s ? JsonValue.Create(s.Trim()) : v;
            case "lower": return (v, _) => Str(v) is { } s ? JsonValue.Create(s.ToLowerInvariant()) : v;
            case "replace":
            {
                var regex = new Regex(n["pattern"]!.GetValue<string>(), RegexOptions.ECMAScript | RegexOptions.CultureInvariant);
                var with = n["with"]?.GetValue<string>() ?? "";
                return (v, _) => Str(v) is { } s ? JsonValue.Create(regex.Replace(s, _ => with)) : v;
            }
            case "round":
            {
                var f = Math.Pow(10, n["digits"]?.GetValue<int>() ?? 0);
                // JavaScript Math.round: half up.
                return (v, _) => v is JsonValue x && x.GetValueKind() == JsonValueKind.Number
                    ? JsonValue.Create(Math.Floor(x.GetValue<double>() * f + 0.5) / f)
                    : v;
            }
            default: throw new InvalidOperationException($"unknown normalizer op: {op}");
        }
    }

    public static JsonPathSegment[] Segments(JsonNode? array) =>
        array!.AsArray().Select(s => s!.GetValueKind() == JsonValueKind.Number ? (JsonPathSegment)s.GetValue<int>() : s.GetValue<string>()).ToArray();

    /// <summary>A diff result in the vectors' shape: equal, summary, changes (only the keys set), tree as [status, path].</summary>
    public static JsonObject Shape(JsonDiffResult r)
    {
        var changes = new JsonArray();
        foreach (var c in r.Changes)
        {
            var o = new JsonObject { ["kind"] = Lower(c.Kind), ["path"] = c.Path };
            if (c.OldPath is not null) o["oldPath"] = c.OldPath;
            if (c.HasOldValue) o["oldValue"] = c.OldValue?.DeepClone();
            if (c.HasNewValue) o["newValue"] = c.NewValue?.DeepClone();
            changes.Add(o);
        }
        var tree = new JsonArray();
        void Walk(JsonDiffNode n)
        {
            tree.Add(new JsonArray(Lower(n.Status), n.Path));
            if (n.Children is null) return;
            foreach (var c in n.Children) Walk(c);
        }
        Walk(r.Tree);
        return new JsonObject
        {
            ["equal"] = r.Equal,
            ["summary"] = new JsonObject { ["added"] = r.Summary.Added, ["removed"] = r.Summary.Removed, ["changed"] = r.Summary.Changed, ["total"] = r.Summary.Total },
            ["changes"] = changes,
            ["tree"] = tree,
        };
    }

    private static string Lower<T>(T e) where T : Enum => e.ToString().ToLowerInvariant();

    /// <summary>
    /// The web runner's <c>JSON.stringify(got) === JSON.stringify(want)</c>: key order counts, and a number is its
    /// double value, so <c>1</c> and <c>1.0</c> agree as they do in JavaScript.
    /// </summary>
    public static string Canon(JsonNode? n)
    {
        var sb = new StringBuilder();
        Write(n, sb);
        return sb.ToString();
    }

    private static void Write(JsonNode? n, StringBuilder sb)
    {
        switch (n)
        {
            case null: sb.Append("null"); break;
            case JsonObject o:
                sb.Append('{');
                var first = true;
                foreach (var (k, v) in o)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonSerializer.Serialize(k)).Append(':');
                    Write(v, sb);
                }
                sb.Append('}');
                break;
            case JsonArray a:
                sb.Append('[');
                for (var i = 0; i < a.Count; i++) { if (i > 0) sb.Append(','); Write(a[i], sb); }
                sb.Append(']');
                break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                var d = v.TryGetValue<double>(out var x) ? x : double.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                break;
            default: sb.Append(n.ToJsonString()); break;
        }
    }
}
