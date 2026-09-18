using Birko.Structures.Caches;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Caches;

public class LruCacheTests
{
    [Fact]
    public void Put_And_Get()
    {
        var cache = new LruCache<string, int>(3);
        cache.Put("a", 1);
        cache.Put("b", 2);

        cache.TryGet("a", out var val).Should().BeTrue();
        val.Should().Be(1);
    }

    [Fact]
    public void Evicts_LeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Put("a", 1);
        cache.Put("b", 2);
        cache.Put("c", 3); // evicts "a"

        cache.ContainsKey("a").Should().BeFalse();
        cache.ContainsKey("b").Should().BeTrue();
        cache.ContainsKey("c").Should().BeTrue();
    }

    [Fact]
    public void Get_UpdatesRecency()
    {
        var cache = new LruCache<string, int>(2);
        cache.Put("a", 1);
        cache.Put("b", 2);
        cache.TryGet("a", out _); // "a" is now most recent
        cache.Put("c", 3); // evicts "b" (not "a")

        cache.ContainsKey("a").Should().BeTrue();
        cache.ContainsKey("b").Should().BeFalse();
    }

    [Fact]
    public void Count_TracksItems()
    {
        var cache = new LruCache<int, int>(5);
        cache.Put(1, 1);
        cache.Put(2, 2);
        cache.Count.Should().Be(2);
        cache.Remove(1);
        cache.Count.Should().Be(1);
    }
}
