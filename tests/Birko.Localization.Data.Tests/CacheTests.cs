using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Data.Tests;

public class CacheTests
{
    [Fact]
    public void CachedResult_IsReturnedOnSecondCall()
    {
        var store = new TestTranslationStore();
        store.Seed(new[]
        {
            new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "en", Value = "Hello" },
        });
        var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.FromMinutes(5));

        // First call loads from store
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");

        // Second call should use cache (even if we could modify store, the cache holds the old value)
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");
    }

    [Fact]
    public void InvalidateCache_ForcesReload()
    {
        var store = new TestTranslationStore();
        store.Seed(new[]
        {
            new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "en", Value = "Hello" },
        });
        var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.FromMinutes(5));

        // Load into cache
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");

        // Invalidate
        provider.InvalidateCache("en");

        // Should reload from store (still returns "Hello" since store hasn't changed)
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");
    }

    [Fact]
    public void InvalidateCache_All_ClearsEverything()
    {
        var store = new TestTranslationStore();
        store.Seed(new[]
        {
            new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "en", Value = "Hello" },
            new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "sk", Value = "Ahoj" },
        });
        var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.FromMinutes(5));

        // Load both cultures
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en"));
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("sk"));

        // Invalidate all
        provider.InvalidateCache();

        // Both should reload (still valid since store unchanged)
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("sk")).Should().Be("Ahoj");
    }

    [Fact]
    public void NoCaching_AlwaysReadsFromStore()
    {
        var store = new TestTranslationStore();
        store.Seed(new[]
        {
            new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "en", Value = "Hello" },
        });
        var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.Zero);

        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");
    }
}
