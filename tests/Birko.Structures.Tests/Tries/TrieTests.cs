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
}
