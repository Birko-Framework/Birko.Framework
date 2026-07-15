using System;
using System.Threading.Tasks;
using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Data.Tests;

/// <summary>
/// CR-L278: the original cache tests never mutated the store between reads, so they couldn't tell a cache
/// hit from a fresh read. These mutate a seeded model's Value behind the cache (the cache holds copied
/// string values, so a stale value proves a hit) and assert: cached value survives a mutation, invalidation
/// forces a reload, and the TTL expires.
/// </summary>
public class CacheTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Sk = CultureInfo.GetCultureInfo("sk");

    [Fact]
    public void CachedValue_SurvivesStoreMutation_UntilInvalidated()
    {
        var store = new TestTranslationStore();
        var model = new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "en", Value = "Hello" };
        store.Seed(new[] { model });
        var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.FromMinutes(5));

        provider.GetTranslation("greeting", En).Should().Be("Hello");

        // Mutate the store behind the cache.
        model.Value = "Hi";

        provider.GetTranslation("greeting", En).Should().Be("Hello", "the cached value is returned, not a fresh read");

        provider.InvalidateCache("en");

        provider.GetTranslation("greeting", En).Should().Be("Hi", "invalidation forces a reload from the store");
    }

    [Fact]
    public void InvalidateCache_All_ReloadsEveryCulture()
    {
        var store = new TestTranslationStore();
        var en = new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "en", Value = "Hello" };
        var sk = new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "sk", Value = "Ahoj" };
        store.Seed(new[] { en, sk });
        var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.FromMinutes(5));

        provider.GetTranslation("greeting", En).Should().Be("Hello");
        provider.GetTranslation("greeting", Sk).Should().Be("Ahoj");

        en.Value = "Hi";
        sk.Value = "Cau";
        provider.InvalidateCache();

        provider.GetTranslation("greeting", En).Should().Be("Hi");
        provider.GetTranslation("greeting", Sk).Should().Be("Cau");
    }

    [Fact]
    public async Task Cache_ExpiresAfterTtl()
    {
        var store = new TestTranslationStore();
        var model = new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "en", Value = "Hello" };
        store.Seed(new[] { model });
        var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.FromMilliseconds(50));

        provider.GetTranslation("greeting", En).Should().Be("Hello");
        model.Value = "Hi";

        await Task.Delay(150);

        provider.GetTranslation("greeting", En).Should().Be("Hi", "the cache entry expired after its TTL");
    }

    [Fact]
    public void NoCaching_AlwaysReadsFromStore()
    {
        var store = new TestTranslationStore();
        var model = new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "en", Value = "Hello" };
        store.Seed(new[] { model });
        var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.Zero);

        provider.GetTranslation("greeting", En).Should().Be("Hello");

        model.Value = "Hi";

        provider.GetTranslation("greeting", En).Should().Be("Hi", "with caching disabled every read hits the store");
    }
}
