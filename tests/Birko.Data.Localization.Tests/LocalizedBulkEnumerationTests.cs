using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// CR-M100: the bulk Create/Update/Delete(IEnumerable&lt;T&gt;) overloads passed the source to the
/// inner store and then iterated it again for translations — enumerating a lazy/one-shot IEnumerable
/// twice (re-running or exhausting it). The source is now materialized once at method entry.
/// </summary>
public class LocalizedBulkEnumerationTests
{
    /// <summary>Counts how many times it is enumerated (not an IList, so the wrapper must ToList it).</summary>
    private sealed class CountingEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _inner;
        public int EnumerationCount { get; private set; }
        public CountingEnumerable(IEnumerable<T> inner) => _inner = inner;
        public IEnumerator<T> GetEnumerator() { EnumerationCount++; return _inner.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static TestEntityLocalizationContext EnContext() => new()
    {
        CurrentCulture = new CultureInfo("en"),
        DefaultCulture = new CultureInfo("en"),
    };

    private static CountingEnumerable<TestLocalizableModel> OneItem() =>
        new(new[] { new TestLocalizableModel { Name = "a", Description = "d", Code = "c" } });

    [Fact]
    public void Sync_Create_enumerates_the_source_once()
    {
        var wrapper = new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            new InMemoryBulkStore<TestLocalizableModel>(),
            new InMemoryBulkStore<EntityTranslationModel>(),
            EnContext());
        var source = OneItem();

        wrapper.Create(source);

        source.EnumerationCount.Should().Be(1);
    }

    [Fact]
    public async Task Async_Create_enumerates_the_source_once()
    {
        var wrapper = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            new InMemoryAsyncBulkStore<TestLocalizableModel>(),
            new InMemoryAsyncBulkStore<EntityTranslationModel>(),
            EnContext());
        var source = OneItem();

        await wrapper.CreateAsync(source);

        source.EnumerationCount.Should().Be(1);
    }
}
