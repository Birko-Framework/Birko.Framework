using System.Text.Json.Nodes;
using Birko.Xaml.Core.Diff;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Core.Tests;

/// <summary>TASK-771: what the shared vectors do not hold — the .NET surface, empty input, DTDs, and an XML diff end to end.</summary>
public class XmlTreeTests
{
    private const string XsiNs = "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"a\":1}")]
    [InlineData("<a><b></a>")]
    public void Text_that_is_not_xml_gives_null_and_never_throws(string text)
    {
        XmlTree.ToTree(text).Should().BeNull();
        XmlTree.TryToTree(text, out _).Should().BeFalse();
        XmlTree.ToLines(text).Should().BeNull();
    }

    [Fact]
    public void TryToTree_tells_a_nil_root_from_text_that_is_not_xml()
    {
        XmlTree.TryToTree($"<r {XsiNs} xsi:nil=\"true\"/>", out var tree).Should().BeTrue();
        tree.Should().BeNull("a nil root reads as null");
        XmlTree.ToTree($"<r {XsiNs} xsi:nil=\"true\"/>").Should().BeNull("ToTree cannot tell them apart, as on the web");
    }

    [Fact]
    public void A_dtd_is_ignored_and_never_resolved()
    {
        // An external entity would read a file if the DTD were processed. It is ignored, so the reference is undeclared
        // and the text is refused rather than expanded.
        const string xxe = "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><r><a>&x;</a></r>";
        XmlTree.ToTree(xxe).Should().BeNull();

        XmlTree.ToTree("<!DOCTYPE r><r><a>1</a><b>2</b></r>")!.ToJsonString().Should().Be("{\"a\":\"1\",\"b\":\"2\"}");
    }

    [Fact]
    public void Two_xml_bodies_diff_by_path()
    {
        const string a = "<R><Offices><Count>3</Count></Offices><Empty/></R>";
        const string b = "<R><Offices><Count>4</Count></Offices><Empty/><New>1</New></R>";

        var r = JsonDiff.Diff(XmlTree.ToTree(a), XmlTree.ToTree(b));

        r.Changes.Select(c => c.ToString()).Should().Equal("changed Offices.Count", "added New");
    }

    [Fact]
    public void Lines_carry_the_paths_a_diff_reports()
    {
        const string b = "<R><Offices><Count>4</Count></Offices><Empty/><New><X>1</X></New></R>";
        var changed = JsonDiff.Diff(XmlTree.ToTree("<R><Offices><Count>3</Count></Offices><Empty/></R>"), XmlTree.ToTree(b))
            .Changes.Select(c => c.Path).ToHashSet();

        var marked = XmlTree.ToLines(b)!
            .Where(l => l.Paths.Any(p => changed.Contains(JsonPath.Format(p))))
            .Select(l => l.Text.Trim());

        marked.Should().Equal("<Count>4</Count>", "<New>", "</New>");
    }

    [Fact]
    public void Default_simple_item_names_are_xmlserializers()
    {
        XmlTree.DefaultSimpleItemNames.Should().Equal("string", "int", "long", "decimal", "double", "boolean", "dateTime");
    }

    [Fact]
    public void Values_stay_text()
    {
        var tree = XmlTree.ToTree("<r><n>3</n><b>false</b></r>")!;

        tree["n"]!.GetValueKind().Should().Be(System.Text.Json.JsonValueKind.String);
        JsonDiff.Diff(tree, JsonNode.Parse("{\"n\":3,\"b\":false}")).Equal.Should().BeFalse("XML is compared with XML");
    }
}
