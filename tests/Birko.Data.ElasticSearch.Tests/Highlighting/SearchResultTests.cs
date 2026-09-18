using Birko.Data.ElasticSearch.Highlighting;
using FluentAssertions;
using System.Collections.Generic;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests.Highlighting;

public class SearchResultTests
{
    private class TestDocument
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void SearchResult_StoresDocument()
    {
        var doc = new TestDocument { Name = "Test" };
        var highlights = new Dictionary<string, IReadOnlyList<string>>();

        var result = new SearchResult<TestDocument>(doc, highlights, 1.5);

        result.Document.Should().BeSameAs(doc);
    }

    [Fact]
    public void SearchResult_StoresHighlights()
    {
        var doc = new TestDocument { Name = "Test" };
        var fragments = new List<string> { "highlighted <em>test</em>" };
        var highlights = new Dictionary<string, IReadOnlyList<string>>
        {
            { "Name", fragments }
        };

        var result = new SearchResult<TestDocument>(doc, highlights, null);

        result.Highlights.Should().ContainKey("Name");
        result.Highlights["Name"].Should().HaveCount(1);
    }

    [Fact]
    public void SearchResult_StoresScore()
    {
        var doc = new TestDocument();
        var highlights = new Dictionary<string, IReadOnlyList<string>>();

        var result = new SearchResult<TestDocument>(doc, highlights, 2.75);

        result.Score.Should().Be(2.75);
    }

    [Fact]
    public void SearchResult_NullScore_IsNull()
    {
        var doc = new TestDocument();
        var highlights = new Dictionary<string, IReadOnlyList<string>>();

        var result = new SearchResult<TestDocument>(doc, highlights, null);

        result.Score.Should().BeNull();
    }
}

public class HighlightedSearchResultsTests
{
    private class TestDocument
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void HighlightedSearchResults_StoresHits()
    {
        var hits = new List<SearchResult<TestDocument>>
        {
            new SearchResult<TestDocument>(
                new TestDocument { Name = "A" },
                new Dictionary<string, IReadOnlyList<string>>(),
                1.0),
            new SearchResult<TestDocument>(
                new TestDocument { Name = "B" },
                new Dictionary<string, IReadOnlyList<string>>(),
                0.5)
        };

        var results = new HighlightedSearchResults<TestDocument>(hits, 100);

        results.Hits.Should().HaveCount(2);
        results.TotalCount.Should().Be(100);
    }

    [Fact]
    public void HighlightedSearchResults_EmptyHits_Works()
    {
        var hits = new List<SearchResult<TestDocument>>();

        var results = new HighlightedSearchResults<TestDocument>(hits, 0);

        results.Hits.Should().BeEmpty();
        results.TotalCount.Should().Be(0);
    }

    [Fact]
    public void HighlightedSearchResults_TotalCountCanExceedHitCount()
    {
        var hits = new List<SearchResult<TestDocument>>
        {
            new SearchResult<TestDocument>(
                new TestDocument(),
                new Dictionary<string, IReadOnlyList<string>>(),
                1.0)
        };

        var results = new HighlightedSearchResults<TestDocument>(hits, 500);

        results.Hits.Should().HaveCount(1);
        results.TotalCount.Should().Be(500);
    }
}
