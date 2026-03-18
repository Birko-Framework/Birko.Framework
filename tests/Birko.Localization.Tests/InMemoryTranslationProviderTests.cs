using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class InMemoryTranslationProviderTests
{
    [Fact]
    public void Builder_CreatesProvider()
    {
        var provider = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hello")
            .AddTranslation("sk", "greeting", "Ahoj")
            .Build();

        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("sk")).Should().Be("Ahoj");
    }

    [Fact]
    public void GetTranslation_ReturnsNull_WhenNotFound()
    {
        var provider = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hello")
            .Build();

        provider.GetTranslation("farewell", CultureInfo.GetCultureInfo("en")).Should().BeNull();
    }

    [Fact]
    public void GetTranslation_ReturnsNull_ForUnknownCulture()
    {
        var provider = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hello")
            .Build();

        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("de")).Should().BeNull();
    }

    [Fact]
    public void GetSupportedCultures_ReturnsAllCultures()
    {
        var provider = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hello")
            .AddTranslation("sk", "greeting", "Ahoj")
            .Build();

        var cultures = provider.GetSupportedCultures();
        cultures.Should().HaveCount(2);
        cultures.Select(c => c.Name).Should().Contain("en").And.Contain("sk");
    }

    [Fact]
    public void GetAll_ReturnsAllKeysForCulture()
    {
        var provider = InMemoryTranslationProvider.Create()
            .AddTranslation("en", "greeting", "Hello")
            .AddTranslation("en", "farewell", "Goodbye")
            .Build();

        var all = provider.GetAll(CultureInfo.GetCultureInfo("en"));
        all.Should().HaveCount(2);
        all["greeting"].Should().Be("Hello");
        all["farewell"].Should().Be("Goodbye");
    }

    [Fact]
    public void GetAll_ReturnsEmpty_ForUnknownCulture()
    {
        var provider = InMemoryTranslationProvider.Create().Build();
        provider.GetAll(CultureInfo.GetCultureInfo("en")).Should().BeEmpty();
    }

    [Fact]
    public void Constructor_FromDictionary()
    {
        var data = new Dictionary<string, IDictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string> { ["greeting"] = "Hello" }
        };
        var provider = new InMemoryTranslationProvider(data);
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");
    }
}
