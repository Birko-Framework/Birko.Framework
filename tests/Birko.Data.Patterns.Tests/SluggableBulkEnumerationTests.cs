using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// CR-M124: the Sluggable bulk wrappers resolved slugs in a foreach over `data` and then passed the
/// same `data` to the inner store — enumerating a lazy/one-shot source twice, so a second enumeration
/// would persist unmutated objects. Create (sync + async) now materializes once.
/// </summary>
public class SluggableBulkEnumerationTests
{
    private class Article : AbstractModel, ISluggable
    {
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? GetSlugSource() => Title;
    }

    private sealed class CountingEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _inner;
        public int EnumerationCount { get; private set; }
        public CountingEnumerable(IEnumerable<T> inner) => _inner = inner;
        public IEnumerator<T> GetEnumerator() { EnumerationCount++; return _inner.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static CountingEnumerable<Article> OneArticle() =>
        new(new[] { new Article { Title = "Hello World" } });

    [Fact]
    public async Task Async_CreateAsync_enumerates_the_source_once_and_persists_the_resolved_slug()
    {
        var store = new AsyncInMemoryStore<Article>();
        var wrapper = new AsyncSluggableBulkStoreWrapper<AsyncInMemoryStore<Article>, Article>(store);
        var source = OneArticle();

        await wrapper.CreateAsync(source);

        source.EnumerationCount.Should().Be(1);
        var stored = await store.ReadAsync(System.Threading.CancellationToken.None);
        stored.Should().ContainSingle().Which.Slug.Should().Be("hello-world", "the persisted item carries the resolved slug");
    }

    [Fact]
    public void Sync_Create_enumerates_the_source_once()
    {
        var store = new InMemoryStore<Article>();
        var wrapper = new SluggableBulkStoreWrapper<InMemoryStore<Article>, Article>(store);
        var source = OneArticle();

        wrapper.Create(source);

        source.EnumerationCount.Should().Be(1);
    }
}
