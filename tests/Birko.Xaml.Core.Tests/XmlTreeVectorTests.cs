using System.Text.Json;
using System.Text.Json.Nodes;
using Birko.Xaml.Core.Diff;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Xaml.Core.Tests;

/// <summary>
/// TASK-771: the C# <see cref="XmlTree"/> against the shared vectors' <c>xmlTree</c> and <c>xmlLines</c> sections, which
/// the web <c>xmlToTree</c> / <c>xmlToLines</c> pass in the Playground (in a browser, on <c>DOMParser</c>). Every failing
/// case is named.
/// </summary>
public class XmlTreeVectorTests(ITestOutputHelper output)
{
    private static XmlTreeOptions? Options(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        var options = new XmlTreeOptions();
        if (o["simpleItemNames"] is JsonArray names) options = options with { SimpleItemNames = names.Select(n => n!.GetValue<string>()).ToArray() };
        if (o["nilAsNull"] is JsonValue nil) options = options with { NilAsNull = nil.GetValue<bool>() };
        if (o["attributePrefix"] is JsonValue prefix) options = options with { AttributePrefix = prefix.GetValue<string>() };
        if (o["textKey"] is JsonValue text) options = options with { TextKey = text.GetValue<string>() };
        return options;
    }

    private static JsonNode? Shape(IReadOnlyList<XmlLine>? lines)
    {
        if (lines is null) return null;
        var array = new JsonArray();
        foreach (var l in lines)
        {
            var paths = new JsonArray();
            foreach (var p in l.Paths)
                paths.Add(new JsonArray(p.Select(s => s.IsIndex ? (JsonNode?)JsonValue.Create(s.Index) : JsonValue.Create(s.Key)).ToArray()));
            array.Add(new JsonObject { ["text"] = l.Text, ["paths"] = paths });
        }
        return array;
    }

    private void Run(string section, Func<string, XmlTreeOptions?, JsonNode?> convert)
    {
        if (DiffVectors.Load(output.WriteLine) is not { } v) return;
        var cases = v[section]!.AsArray();
        cases.Should().NotBeEmpty();
        var failures = new List<string>();
        foreach (var c in cases)
        {
            var name = c!["name"]!.GetValue<string>();
            try
            {
                var got = DiffVectors.Canon(convert(c["xml"]!.GetValue<string>(), Options(c["options"])));
                var want = DiffVectors.Canon(c["expect"]);
                if (got != want) failures.Add($"{name}:\n  got  {got}\n  want {want}");
            }
            catch (Exception e)
            {
                failures.Add($"{name}: threw {e.GetType().Name}: {e.Message}");
            }
        }
        output.WriteLine($"{cases.Count - failures.Count}/{cases.Count} {section} vectors pass");
        failures.Should().BeEmpty();
    }

    [Fact]
    public void Every_xmlTree_vector_gives_the_web_result() => Run("xmlTree", (xml, o) => XmlTree.ToTree(xml, o));

    [Fact]
    public void Every_xmlLines_vector_gives_the_web_result() => Run("xmlLines", (xml, o) => Shape(XmlTree.ToLines(xml, o)));
}
