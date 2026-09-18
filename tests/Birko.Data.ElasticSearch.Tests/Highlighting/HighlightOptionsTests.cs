using Birko.Data.ElasticSearch.Highlighting;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests.Highlighting;

public class HighlightOptionsTests
{
    [Fact]
    public void PreTag_DefaultsToEm()
    {
        var options = new HighlightOptions();

        options.PreTag.Should().Be("<em>");
    }

    [Fact]
    public void PostTag_DefaultsToClosingEm()
    {
        var options = new HighlightOptions();

        options.PostTag.Should().Be("</em>");
    }

    [Fact]
    public void FragmentSize_DefaultsTo150()
    {
        var options = new HighlightOptions();

        options.FragmentSize.Should().Be(150);
    }

    [Fact]
    public void NumberOfFragments_DefaultsTo3()
    {
        var options = new HighlightOptions();

        options.NumberOfFragments.Should().Be(3);
    }

    [Fact]
    public void Fields_DefaultsToEmptyList()
    {
        var options = new HighlightOptions();

        options.Fields.Should().NotBeNull();
        options.Fields.Should().BeEmpty();
    }

    [Fact]
    public void Fields_CanBePopulated()
    {
        var options = new HighlightOptions();
        options.Fields.Add("title");
        options.Fields.Add("body");

        options.Fields.Should().HaveCount(2);
        options.Fields.Should().Contain("title");
        options.Fields.Should().Contain("body");
    }

    [Fact]
    public void PreTag_CanBeCustomized()
    {
        var options = new HighlightOptions { PreTag = "<strong>" };

        options.PreTag.Should().Be("<strong>");
    }

    [Fact]
    public void PostTag_CanBeCustomized()
    {
        var options = new HighlightOptions { PostTag = "</strong>" };

        options.PostTag.Should().Be("</strong>");
    }

    [Fact]
    public void FragmentSize_CanBeSetToNull()
    {
        var options = new HighlightOptions { FragmentSize = null };

        options.FragmentSize.Should().BeNull();
    }

    [Fact]
    public void NumberOfFragments_CanBeSetToNull()
    {
        var options = new HighlightOptions { NumberOfFragments = null };

        options.NumberOfFragments.Should().BeNull();
    }
}
