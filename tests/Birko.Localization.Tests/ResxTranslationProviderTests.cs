using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class ResxTranslationProviderTests
{
    private static string GetTestResourcesPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "TestResources");
    }

    [Fact]
    public void GetTranslation_ReturnsValue()
    {
        var provider = new ResxTranslationProvider(GetTestResourcesPath(), "Messages");
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");
    }

    [Fact]
    public void GetTranslation_Slovak()
    {
        var provider = new ResxTranslationProvider(GetTestResourcesPath(), "Messages");
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("sk")).Should().Be("Ahoj");
        provider.GetTranslation("farewell", CultureInfo.GetCultureInfo("sk")).Should().Be("Dovidenia");
    }

    [Fact]
    public void GetTranslation_ReturnsNull_WhenKeyNotFound()
    {
        var provider = new ResxTranslationProvider(GetTestResourcesPath(), "Messages");
        provider.GetTranslation("nonexistent", CultureInfo.GetCultureInfo("en")).Should().BeNull();
    }

    [Fact]
    public void GetTranslation_ReturnsNull_WhenCultureNotFound()
    {
        var provider = new ResxTranslationProvider(GetTestResourcesPath(), "Messages");
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("de")).Should().BeNull();
    }

    [Fact]
    public void GetSupportedCultures_ListsCultures()
    {
        var provider = new ResxTranslationProvider(GetTestResourcesPath(), "Messages");
        var cultures = provider.GetSupportedCultures();
        cultures.Select(c => c.Name).Should().Contain("en").And.Contain("sk");
    }

    [Fact]
    public void GetAll_ReturnsAllKeys()
    {
        var provider = new ResxTranslationProvider(GetTestResourcesPath(), "Messages");
        var all = provider.GetAll(CultureInfo.GetCultureInfo("en"));
        all.Should().ContainKey("greeting");
        all.Should().ContainKey("farewell");
    }

    [Fact]
    public void Constructor_ThrowsForInvalidPath()
    {
        var act = () => new ResxTranslationProvider("/nonexistent/path", "Messages");
        act.Should().Throw<DirectoryNotFoundException>();
    }
}
