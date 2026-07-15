using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class LocalizerTests
{
    private static InMemoryTranslationProvider CreateProvider()
    {
        return InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hello")
            .AddTranslation("en", "farewell", "Goodbye")
            .AddTranslation("sk", "greeting", "Ahoj")
            .AddTranslation("sk", "farewell", "Dovidenia")
            .AddTranslation("sk-SK", "greeting", "Ahoj (SK)")
            .AddTranslation("en", "welcome", "Welcome, {userName}!")
            .AddTranslation("en", "items", "You have {0} items")
            .Build();
    }

    [Fact]
    public void Get_ReturnsTranslation_ForExactCulture()
    {
        var localizer = new Localizer(CreateProvider());
        var result = localizer.Get("greeting", CultureInfo.GetCultureInfo("en"));
        result.Should().Be("Hello");
    }

    [Fact]
    public void Get_FallsBackToParentCulture()
    {
        var localizer = new Localizer(CreateProvider());
        // sk-SK has "greeting" but not "farewell", should fall back to "sk"
        var result = localizer.Get("farewell", CultureInfo.GetCultureInfo("sk-SK"));
        result.Should().Be("Dovidenia");
    }

    [Fact]
    public void Get_FallsBackToDefaultCulture()
    {
        var settings = LocalizationSettings.Default.WithDefaultCulture(CultureInfo.GetCultureInfo("en"));
        var localizer = new Localizer(CreateProvider(), settings);
        // "de" has no translations, should fall back to "en"
        var result = localizer.Get("greeting", CultureInfo.GetCultureInfo("de"));
        result.Should().Be("Hello");
    }

    [Fact]
    public void Get_ReturnsKey_WhenNotFound_ReturnKeyBehavior()
    {
        var localizer = new Localizer(CreateProvider());
        var result = localizer.Get("nonexistent", CultureInfo.GetCultureInfo("en"));
        result.Should().Be("nonexistent");
    }

    [Fact]
    public void Get_ReturnsEmpty_WhenNotFound_ReturnEmptyBehavior()
    {
        var settings = LocalizationSettings.Default.WithMissingKeyBehavior(MissingKeyBehavior.ReturnEmpty);
        var localizer = new Localizer(CreateProvider(), settings);
        var result = localizer.Get("nonexistent", CultureInfo.GetCultureInfo("en"));
        result.Should().BeEmpty();
    }

    [Fact]
    public void Get_ThrowsKeyNotFoundException_WhenNotFound_ThrowBehavior()
    {
        var settings = LocalizationSettings.Default.WithMissingKeyBehavior(MissingKeyBehavior.ThrowException);
        var localizer = new Localizer(CreateProvider(), settings);
        var act = () => localizer.Get("nonexistent", CultureInfo.GetCultureInfo("en"));
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Get_WithNamedArgs_InterpolatesPlaceholders()
    {
        var localizer = new Localizer(CreateProvider());
        var result = localizer.Get("welcome",
            new Dictionary<string, object?> { ["userName"] = "John" },
            CultureInfo.GetCultureInfo("en"));
        result.Should().Be("Welcome, John!");
    }

    [Fact]
    public void Get_WithPositionalArgs_InterpolatesPlaceholders()
    {
        var localizer = new Localizer(CreateProvider());
        var result = localizer.Get("items", new object[] { 5 }, CultureInfo.GetCultureInfo("en"));
        result.Should().Be("You have 5 items");
    }

    [Fact]
    public void Get_WithKeyPrefix_PrependsPrefix()
    {
        var provider = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "app.greeting", "Hello App")
            .Build();
        var settings = LocalizationSettings.Default.WithKeyPrefix("app");
        var localizer = new Localizer(provider, settings);

        var result = localizer.Get("greeting", CultureInfo.GetCultureInfo("en"));
        result.Should().Be("Hello App");
    }

    [Fact]
    public void HasTranslation_ReturnsTrue_WhenExists()
    {
        var localizer = new Localizer(CreateProvider());
        localizer.HasTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().BeTrue();
    }

    [Fact]
    public void HasTranslation_ReturnsFalse_WhenNotExists()
    {
        var settings = LocalizationSettings.Default.WithFallbackToParentCulture(false);
        var localizer = new Localizer(CreateProvider(), settings);
        localizer.HasTranslation("nonexistent", CultureInfo.GetCultureInfo("en")).Should().BeFalse();
    }

    [Fact]
    public void Get_WithoutParentFallback_DoesNotFallBack()
    {
        var settings = LocalizationSettings.Default.WithFallbackToParentCulture(false);
        var localizer = new Localizer(CreateProvider(), settings);
        // sk-SK doesn't have "farewell", and parent fallback is disabled
        var result = localizer.Get("farewell", CultureInfo.GetCultureInfo("sk-SK"));
        result.Should().Be("farewell"); // returns key
    }

    [Fact]
    public void Get_ExactCulture_TakesPriorityOverParent()
    {
        var localizer = new Localizer(CreateProvider());
        var result = localizer.Get("greeting", CultureInfo.GetCultureInfo("sk-SK"));
        result.Should().Be("Ahoj (SK)");
    }

    [Fact]
    public void Get_NullKey_ThrowsArgumentException()
    {
        var localizer = new Localizer(CreateProvider());
        var act = () => localizer.Get(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Get_PositionalArgs_FormatInResolvedCulture()
    {
        // CR-L276: a decimal positional arg formats in the resolved (de) culture — comma separator.
        var provider = InMemoryTranslationProvider.Create()
            .AddTranslation("de", "price", "Preis: {0}")
            .Build();
        var localizer = new Localizer(provider);

        var result = localizer.Get("price", new object[] { 1.5m }, CultureInfo.GetCultureInfo("de"));

        result.Should().Be("Preis: 1,5");
    }

    [Fact]
    public void Resolve_DefaultCultureReachedAsParent_NotQueriedTwice()
    {
        // CR-L274: with default "sk" and requested "sk-SK", the default is reached as a parent in step 2;
        // step 3 must not query it again.
        var provider = new CountingProvider();
        var settings = LocalizationSettings.Default.WithDefaultCulture(CultureInfo.GetCultureInfo("sk"));
        var localizer = new Localizer(provider, settings);

        localizer.Get("missing", CultureInfo.GetCultureInfo("sk-SK"));

        provider.Queried.Should().Equal("sk-SK", "sk");
        provider.Queried.Count(x => x == "sk").Should().Be(1);
    }

    /// <summary>Records every culture the localizer queries; always misses so the full fallback chain runs.</summary>
    private sealed class CountingProvider : ITranslationProvider
    {
        public List<string> Queried { get; } = new();

        public string? GetTranslation(string key, CultureInfo culture)
        {
            Queried.Add(culture.Name);
            return null;
        }

        public IReadOnlyList<CultureInfo> GetSupportedCultures() => Array.Empty<CultureInfo>();

        public IReadOnlyDictionary<string, string> GetAll(CultureInfo culture) => new Dictionary<string, string>();
    }
}
