using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class CompositeTranslationProviderTests
{
    [Fact]
    public void GetTranslation_ReturnsFromHigherPriorityProvider()
    {
        var primary = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hi")
            .Build();
        var fallback = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hello")
            .AddTranslation("en", "farewell", "Goodbye")
            .Build();

        var composite = new CompositeTranslationProvider(primary, fallback);

        composite.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hi");
        composite.GetTranslation("farewell", CultureInfo.GetCultureInfo("en")).Should().Be("Goodbye");
    }

    [Fact]
    public void GetTranslation_ReturnsNull_WhenNoneHaveKey()
    {
        var provider = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hello")
            .Build();

        var composite = new CompositeTranslationProvider(provider);
        composite.GetTranslation("nonexistent", CultureInfo.GetCultureInfo("en")).Should().BeNull();
    }

    [Fact]
    public void GetSupportedCultures_ReturnsUnion()
    {
        var en = InMemoryTranslationProvider.Create().AddTranslation("en", "a", "1").Build();
        var sk = InMemoryTranslationProvider.Create().AddTranslation("sk", "a", "1").Build();

        var composite = new CompositeTranslationProvider(en, sk);
        var cultures = composite.GetSupportedCultures();
        cultures.Select(c => c.Name).Should().Contain("en").And.Contain("sk");
    }

    [Fact]
    public void GetAll_MergesWithHigherPriorityWinning()
    {
        var primary = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hi")
            .Build();
        var fallback = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hello")
            .AddTranslation("en", "farewell", "Goodbye")
            .Build();

        var composite = new CompositeTranslationProvider(primary, fallback);
        var all = composite.GetAll(CultureInfo.GetCultureInfo("en"));

        all["greeting"].Should().Be("Hi");
        all["farewell"].Should().Be("Goodbye");
    }

    [Fact]
    public void Constructor_ThrowsForEmptyProviders()
    {
        var act = () => new CompositeTranslationProvider();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ThrowsForNull()
    {
        var act = () => new CompositeTranslationProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
