using System.Text.Json.Nodes;

namespace Birko.Xaml.Core.Diff;

/// <summary>The kind of one difference.</summary>
public enum JsonChangeKind { Added, Removed, Changed }

/// <summary>
/// A tree node's status. <see cref="Modified"/> is a container whose own type matches on both sides but which holds a
/// difference below; <see cref="Changed"/> is a value (or a container whose type differs) replaced outright.
/// </summary>
public enum JsonDiffStatus { Equal, Added, Removed, Changed, Modified, Ignored }

/// <summary>A node's shape — side B's when it exists, otherwise side A's.</summary>
public enum JsonNodeType { Object, Array, Value }

/// <summary>How the items of an array are paired.</summary>
public enum JsonArrayMatch
{
    /// <summary>Item n with item n (the default for every array).</summary>
    Index,

    /// <summary>Each item of B with an equal, not yet paired item of A; the rest are added / removed.</summary>
    Unordered,
}

/// <summary>Counts of differences. <see cref="Total"/> is <c>Added + Removed + Changed</c>.</summary>
public sealed record JsonDiffSummary(int Added, int Removed, int Changed)
{
    public int Total => Added + Removed + Changed;

    public static JsonDiffSummary Empty { get; } = new(0, 0, 0);

    public static JsonDiffSummary operator +(JsonDiffSummary a, JsonDiffSummary b) =>
        new(a.Added + b.Added, a.Removed + b.Removed, a.Changed + b.Changed);
}

/// <summary>
/// One difference. <see cref="OldValue"/> is side A's value and <see cref="NewValue"/> side B's, both as received
/// (before normalizing). A removal has no new value and an addition no old one (<see cref="HasOldValue"/> /
/// <see cref="HasNewValue"/>); a JSON <c>null</c> is a value, so it is told apart from "absent" by those flags.
/// </summary>
public sealed class JsonChange
{
    public required JsonChangeKind Kind { get; init; }

    public required string Path { get; init; }

    public required IReadOnlyList<JsonPathSegment> Segments { get; init; }

    /// <summary>Side A's path, set only where it differs from <see cref="Path"/> — an item paired by key or unordered.</summary>
    public string? OldPath { get; init; }

    public bool HasOldValue { get; init; }

    public JsonNode? OldValue { get; init; }

    public bool HasNewValue { get; init; }

    public JsonNode? NewValue { get; init; }

    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()} {Path}";
}

/// <summary>
/// A node of the merged tree of both sides. The children of an added / removed container repeat its status so a
/// renderer can show its content; they are not counted again in <see cref="Summary"/>.
/// </summary>
public sealed class JsonDiffNode
{
    /// <summary>The node's last segment, or <c>null</c> for the root.</summary>
    public JsonPathSegment? Key { get; init; }

    public required string Path { get; init; }

    public required IReadOnlyList<JsonPathSegment> Segments { get; init; }

    /// <summary>
    /// Where the node sits in side A, set only where it differs from <see cref="Segments"/>: below an array paired by
    /// key or unordered, a pair (and everything in it) is placed at its side-B index and a removed item at the end,
    /// under its side-A index.
    /// </summary>
    public IReadOnlyList<JsonPathSegment>? OldSegments { get; init; }

    public required JsonDiffStatus Status { get; init; }

    public required JsonNodeType Type { get; init; }

    public bool HasOld { get; init; }

    public bool HasNew { get; init; }

    public JsonNode? OldValue { get; init; }

    public JsonNode? NewValue { get; init; }

    /// <summary>The child nodes of a container, in order; <c>null</c> for a value or a node replaced outright.</summary>
    public IReadOnlyList<JsonDiffNode>? Children { get; init; }

    /// <summary>Counts for this node and everything below it.</summary>
    public required JsonDiffSummary Summary { get; init; }
}

/// <summary>Maps a value on both sides before the comparison (web <c>JsonNormalizer</c>).</summary>
/// <param name="Path">Path pattern the normalizer applies to.</param>
/// <param name="Normalize">
/// Returns the value to compare instead, given the value and its path. Applied to both sides; several matching
/// normalizers chain in order. It must not modify the node it is given — return a new one.
/// </param>
public sealed record JsonNormalizer(string Path, Func<JsonNode?, string, JsonNode?> Normalize);

/// <summary>How the array at <see cref="Path"/> pairs its items (web <c>JsonArrayRule</c>).</summary>
public sealed record JsonArrayRule(string Path)
{
    public JsonArrayMatch Match { get; init; } = JsonArrayMatch.Index;

    /// <summary>
    /// Pairs object items by this property instead, so a reordered list of records diffs record by record. Takes
    /// precedence over <see cref="Match"/>. Items without the property fall back to unordered pairing.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Pairs items by what this returns instead of by <see cref="Key"/> (web: <c>key</c> as a function). Returning
    /// <c>null</c> means "no key": the item falls back to unordered pairing. A returned <see cref="JsonNode"/> is
    /// compared by its JSON value, anything else with <see cref="object.Equals(object?)"/>.
    /// </summary>
    public Func<JsonNode?, object?>? KeySelector { get; init; }
}

/// <summary>Options of <see cref="JsonDiff.Diff"/>.</summary>
public sealed class JsonDiffOptions
{
    /// <summary>Path patterns left out of the comparison. They appear in the tree as ignored and are never counted.</summary>
    public IList<string> Ignore { get; init; } = new List<string>();

    public IList<JsonNormalizer> Normalize { get; init; } = new List<JsonNormalizer>();

    /// <summary>Array pairing rules; the first whose pattern matches an array governs it.</summary>
    public IList<JsonArrayRule> Arrays { get; init; } = new List<JsonArrayRule>();
}

/// <summary>The comparison three ways: a flat <see cref="Changes"/> list, the merged <see cref="Tree"/>, and counts.</summary>
public sealed class JsonDiffResult
{
    public required bool Equal { get; init; }

    public required IReadOnlyList<JsonChange> Changes { get; init; }

    public required JsonDiffNode Tree { get; init; }

    public required JsonDiffSummary Summary { get; init; }
}
