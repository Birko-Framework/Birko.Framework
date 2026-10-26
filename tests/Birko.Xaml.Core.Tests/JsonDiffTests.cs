using System.Text.Json.Nodes;
using Birko.Xaml.Core.Diff;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Core.Tests;

/// <summary>
/// TASK-770: what the shared vectors cannot express as data — function normalizers and key selectors, the .NET
/// surface (presence flags, segments, CLR-backed values), and that the inputs are left alone.
/// </summary>
public class JsonDiffTests
{
    private static JsonNode? J(string json) => JsonNode.Parse(json);

    private static string[] Brief(JsonDiffResult r) => r.Changes.Select(c => c.ToString()).ToArray();

    [Fact]
    public void A_normalizer_gets_the_path_once_per_side()
    {
        var paths = new List<string>();
        JsonDiff.Diff(J("""{"a":1}"""), J("""{"a":1}"""), new JsonDiffOptions
        {
            Normalize = [new JsonNormalizer("a", (v, p) => { paths.Add(p); return v; })],
        });

        paths.Should().Equal("a", "a");
    }

    [Fact]
    public void A_normalizer_returning_a_clr_value_compares_by_json_value()
    {
        // The tester's normalizers return JsonValue.Create(...), not parsed nodes: 1 (parsed) and 1.0 (CLR) are equal.
        var r = JsonDiff.Diff(J("""{"p":1.0001}"""), J("""{"p":1}"""), new JsonDiffOptions
        {
            Normalize = [new JsonNormalizer("p", (v, _) => JsonValue.Create(Math.Round(v!.GetValue<double>(), 2)))],
        });

        r.Equal.Should().BeTrue();
    }

    [Fact]
    public void Normalizing_does_not_change_the_reported_values()
    {
        var r = JsonDiff.Diff(J("""{"n":" A ","m":"x"}"""), J("""{"n":"a","m":"y"}"""), new JsonDiffOptions
        {
            Normalize = [new JsonNormalizer("**", (v, _) => v is JsonValue s && s.TryGetValue<string>(out var str) ? JsonValue.Create(str.Trim().ToLowerInvariant()) : v)],
        });

        Brief(r).Should().Equal("changed m");
        r.Changes[0].OldValue!.GetValue<string>().Should().Be("x");
        r.Tree.Children![0].OldValue!.GetValue<string>().Should().Be(" A ", "the tree keeps the values as received too");
    }

    [Fact]
    public void A_key_selector_pairs_as_a_key_property_does()
    {
        var a = J("""{"Items":[{"Id":1,"v":1},{"Id":2,"v":2},{"Id":4,"v":4}]}""");
        var b = J("""{"Items":[{"Id":2,"v":2},{"Id":1,"v":9},{"Id":3,"v":3}]}""");

        var byProperty = JsonDiff.Diff(a, b, new JsonDiffOptions { Arrays = [new JsonArrayRule("Items") { Key = "Id" }] });
        var bySelector = JsonDiff.Diff(a, b, new JsonDiffOptions { Arrays = [new JsonArrayRule("Items") { KeySelector = item => item!["Id"]!.GetValue<int>() }] });

        Brief(bySelector).Should().Equal(Brief(byProperty));
        Brief(byProperty).Should().Equal("changed Items[1].v", "added Items[2]", "removed Items[2]");
        bySelector.Changes[0].OldPath.Should().Be("Items[0].v");
    }

    [Fact]
    public void A_key_selector_returning_null_falls_back_to_unordered_pairing()
    {
        var r = JsonDiff.Diff(J("""{"l":[{"k":1},"x"]}"""), J("""{"l":["x",{"k":1}]}"""), new JsonDiffOptions
        {
            Arrays = [new JsonArrayRule("l") { KeySelector = item => item is JsonObject o ? o["k"] : null }],
        });

        r.Equal.Should().BeTrue();
    }

    [Fact]
    public void A_clr_key_and_a_json_key_of_the_same_number_pair()
    {
        // Side A's key is the parsed JsonNode, side B's a CLR long: both are the number 1.
        var r = JsonDiff.Diff(J("""{"l":[{"k":1,"v":1}]}"""), J("""{"l":[{"k":1,"v":2}]}"""), new JsonDiffOptions
        {
            Arrays = [new JsonArrayRule("l") { KeySelector = item => item!["v"]!.GetValue<int>() == 1 ? item["k"] : (object)1L }],
        });

        Brief(r).Should().Equal("changed l[0].v");
    }

    [Fact]
    public void Presence_flags_tell_a_json_null_from_an_absent_value()
    {
        var added = JsonDiff.Diff(J("{}"), J("""{"a":null}""")).Changes.Single();
        added.HasNewValue.Should().BeTrue();
        added.NewValue.Should().BeNull();
        added.HasOldValue.Should().BeFalse();

        var removed = JsonDiff.Diff(J("""{"a":1}"""), J("{}")).Changes.Single();
        removed.HasOldValue.Should().BeTrue();
        removed.HasNewValue.Should().BeFalse();
        removed.NewValue.Should().BeNull();
    }

    [Fact]
    public void A_change_carries_its_segments_and_a_paired_node_its_side_a_segments()
    {
        var r = JsonDiff.Diff(J("""{"t":["a","b"]}"""), J("""{"t":["b","a"]}"""), new JsonDiffOptions
        {
            Arrays = [new JsonArrayRule("t") { Match = JsonArrayMatch.Unordered }],
        });

        r.Equal.Should().BeTrue();
        var first = r.Tree.Children![0].Children![0];
        first.Segments.Should().Equal("t", 0);
        first.OldSegments.Should().Equal("t", 1);
        first.Key.Should().Be((JsonPathSegment)0);
        r.Tree.Key.Should().BeNull("the root has no key");
    }

    [Fact]
    public void Two_null_roots_are_equal_and_null_against_a_value_is_a_change()
    {
        JsonDiff.Diff(null, null).Equal.Should().BeTrue();
        Brief(JsonDiff.Diff(null, J("0"))).Should().Equal("changed ");
    }

    [Fact]
    public void Diffing_leaves_both_inputs_unchanged_and_works_on_nested_nodes()
    {
        var a = J("""{"wrap":{"x":[1,2]}}""")!;
        var b = J("""{"wrap":{"x":[2]}}""")!;
        var before = (a.ToJsonString(), b.ToJsonString());

        // Nodes that already have a parent, as a caller passes when diffing a sub-tree.
        var r = JsonDiff.Diff(a["wrap"], b["wrap"]);

        Brief(r).Should().Equal("changed x[0]", "removed x[1]");
        (a.ToJsonString(), b.ToJsonString()).Should().Be(before);
    }

    [Fact]
    public void A_key_and_an_index_are_different_segments()
    {
        ((JsonPathSegment)"0").Should().NotBe((JsonPathSegment)0);
        JsonPath.Format(new JsonPathSegment[] { "0" }).Should().Be("[\"0\"]");
        JsonPath.Format(new JsonPathSegment[] { 0 }).Should().Be("[0]");
    }

    [Fact]
    public void An_unknown_wildcard_is_refused_even_when_it_would_match_nothing()
    {
        var act = () => JsonDiff.Diff(J("{}"), J("{}"), new JsonDiffOptions { Arrays = [new JsonArrayRule("a[b]")] });

        act.Should().Throw<FormatException>().WithMessage("*a[b]*");
    }
}
