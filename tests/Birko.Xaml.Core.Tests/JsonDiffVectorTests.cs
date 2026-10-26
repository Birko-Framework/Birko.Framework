using System.Text.Json.Nodes;
using Birko.Xaml.Core.Diff;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Xaml.Core.Tests;

/// <summary>
/// TASK-770: the C# <see cref="JsonDiff"/> against the shared vectors the web engine passes (Birko.Web.Core's
/// diff-vectors.json, run there by the Playground's diff-vectors-smoke). Every failing case is named, so one run
/// shows the whole gap rather than the first difference.
/// </summary>
public class JsonDiffVectorTests(ITestOutputHelper output)
{
    [Fact]
    public void Paths_format_as_the_web_formats_them()
    {
        if (DiffVectors.Load(output.WriteLine) is not { } v) return;
        var failures = new List<string>();
        foreach (var c in v["paths"]!["format"]!.AsArray())
        {
            var got = JsonPath.Format(DiffVectors.Segments(c!["segments"]));
            var want = c["expect"]!.GetValue<string>();
            if (got != want) failures.Add($"{c["segments"]!.ToJsonString()}: got {got}, want {want}");
        }
        failures.Should().BeEmpty();
    }

    [Fact]
    public void Patterns_match_as_the_web_matches_them()
    {
        if (DiffVectors.Load(output.WriteLine) is not { } v) return;
        var failures = new List<string>();
        foreach (var c in v["paths"]!["match"]!.AsArray())
        {
            var pattern = c!["pattern"]!.GetValue<string>();
            var got = JsonPath.Matches(pattern, DiffVectors.Segments(c["segments"]));
            if (got != c["expect"]!.GetValue<bool>()) failures.Add($"{pattern} {c["segments"]!.ToJsonString()}: got {got}");
        }
        failures.Should().BeEmpty();
    }

    [Fact]
    public void A_malformed_pattern_throws_before_anything_is_compared()
    {
        if (DiffVectors.Load(output.WriteLine) is not { } v) return;
        foreach (var p in v["paths"]!["malformed"]!.AsArray())
        {
            var pattern = p!.GetValue<string>();
            var act = () => JsonDiff.Diff(new JsonObject(), new JsonObject(), new JsonDiffOptions { Ignore = [pattern] });
            act.Should().Throw<FormatException>(pattern);
        }
    }

    [Fact]
    public void Every_json_vector_gives_the_web_engines_result()
    {
        if (DiffVectors.Load(output.WriteLine) is not { } v) return;
        var cases = v["json"]!.AsArray();
        cases.Should().NotBeEmpty();
        var failures = new List<string>();
        foreach (var c in cases)
        {
            var name = c!["name"]!.GetValue<string>();
            try
            {
                var r = JsonDiff.Diff(c["a"], c["b"], DiffVectors.Options(c["options"]));
                var got = DiffVectors.Canon(DiffVectors.Shape(r));
                var want = DiffVectors.Canon(c["expect"]);
                if (got != want) failures.Add($"{name}:\n  got  {got}\n  want {want}");
            }
            catch (Exception e)
            {
                failures.Add($"{name}: threw {e.GetType().Name}: {e.Message}");
            }
        }
        output.WriteLine($"{cases.Count - failures.Count}/{cases.Count} json vectors pass");
        failures.Should().BeEmpty();
    }
}
