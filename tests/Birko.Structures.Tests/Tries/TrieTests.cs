using Birko.Structures.Tries;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Tries;

public class TrieTests
{
    [Fact]
    public void Insert_And_Search()
    {
        var trie = new Trie();
        trie.Insert("hello");
        trie.Insert("help");
        trie.Insert("world");

        trie.Search("hello").Should().BeTrue();
        trie.Search("help").Should().BeTrue();
        trie.Search("hel").Should().BeFalse();
        trie.Count.Should().Be(3);
    }

    [Fact]
    public void GetWordsWithPrefix()
    {
        var trie = new Trie();
        trie.Insert("hello");
        trie.Insert("help");
        trie.Insert("world");

        var matches = trie.GetWordsWithPrefix("hel");
        matches.Should().HaveCount(2);
        matches.Should().Contain("hello");
        matches.Should().Contain("help");
    }

    [Fact]
    public void Remove_DecreasesCount()
    {
        var trie = new Trie();
        trie.Insert("hello");
        trie.Insert("help");
        trie.Remove("hello").Should().BeTrue();
        trie.Count.Should().Be(1);
        trie.Search("hello").Should().BeFalse();
        trie.Search("help").Should().BeTrue();
    }
}

public class CompressedTrieTests
{
    [Fact]
    public void Insert_And_Search()
    {
        var trie = new CompressedTrie();
        trie.Insert("test");
        trie.Insert("testing");
        trie.Insert("team");

        trie.Search("test").Should().BeTrue();
        trie.Search("testing").Should().BeTrue();
        trie.Search("team").Should().BeTrue();
        trie.Search("tea").Should().BeFalse();
        trie.Count.Should().Be(3);
    }

    [Fact]
    public void StartsWith_Works()
    {
        var trie = new CompressedTrie();
        trie.Insert("hello");
        trie.StartsWith("hel").Should().BeTrue();
        trie.StartsWith("xyz").Should().BeFalse();
    }

    [Fact]
    public void GetWordsWithPrefix_MidEdgePrefix_ReturnsFullWords()
    {
        // CR-H142: "ap" lands partway through the "apple" edge; the result must be the full word,
        // not the truncated query prefix.
        var trie = new CompressedTrie();
        trie.Insert("apple");

        trie.GetWordsWithPrefix("ap").Should().BeEquivalentTo(new[] { "apple" });
    }

    [Fact]
    public void GetWordsWithPrefix_MidEdgePrefix_MultipleWords()
    {
        var trie = new CompressedTrie();
        trie.Insert("apple");
        trie.Insert("applet");
        trie.Insert("application");
        trie.Insert("banana");

        trie.GetWordsWithPrefix("ap").Should().BeEquivalentTo(new[] { "apple", "applet", "application" });
    }

    [Fact]
    public void GetWordsWithPrefix_NodeBoundaryPrefix_StillWorks()
    {
        var trie = new CompressedTrie();
        trie.Insert("app");
        trie.Insert("apple");

        trie.GetWordsWithPrefix("app").Should().BeEquivalentTo(new[] { "app", "apple" });
    }

    [Fact]
    public void GetAllWords_ReturnsEveryInsertedWord()
    {
        var trie = new CompressedTrie();
        trie.Insert("test");
        trie.Insert("testing");
        trie.Insert("team");

        trie.GetAllWords().Should().BeEquivalentTo(new[] { "test", "testing", "team" });
    }

    [Fact]
    public void GetWordsWithPrefix_NoMatch_ReturnsEmpty()
    {
        var trie = new CompressedTrie();
        trie.Insert("apple");

        trie.GetWordsWithPrefix("xyz").Should().BeEmpty();
    }
}
