using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class JsonTranslationProviderTests
{
    private static string GetTestResourcesPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "TestResources");
    }

    [Fact]
    public void GetTranslation_ReturnsFlatKey()
    {
        var provider = new JsonTranslationProvider(GetTestResourcesPath());
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");
    }

    [Fact]
    public void GetTranslation_ReturnsNestedKey_WithDotNotation()
    {
        var provider = new JsonTranslationProvider(GetTestResourcesPath());
        provider.GetTranslation("errors.notFound", CultureInfo.GetCultureInfo("en")).Should().Be("Not found");
    }

    [Fact]
    public void GetTranslation_ReturnsNull_WhenKeyNotFound()
    {
        var provider = new JsonTranslationProvider(GetTestResourcesPath());
        provider.GetTranslation("nonexistent", CultureInfo.GetCultureInfo("en")).Should().BeNull();
    }

    [Fact]
    public void GetTranslation_ReturnsNull_WhenCultureNotFound()
    {
        var provider = new JsonTranslationProvider(GetTestResourcesPath());
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("de")).Should().BeNull();
    }

    [Fact]
    public void GetTranslation_Slovak_ReturnsCorrectValue()
    {
        var provider = new JsonTranslationProvider(GetTestResourcesPath());
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("sk")).Should().Be("Ahoj");
        provider.GetTranslation("errors.forbidden", CultureInfo.GetCultureInfo("sk")).Should().Be("Prístup zamietnutý");
    }

    [Fact]
    public void GetTranslation_RegionalCulture()
    {
        var provider = new JsonTranslationProvider(GetTestResourcesPath());
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("sk-SK")).Should().Be("Ahoj (SK)");
        provider.GetTranslation("regional_only", CultureInfo.GetCultureInfo("sk-SK")).Should().Be("Len pre Slovensko");
    }

    [Fact]
    public void GetSupportedCultures_ListsAvailableFiles()
    {
        var provider = new JsonTranslationProvider(GetTestResourcesPath());
        var cultures = provider.GetSupportedCultures();
        cultures.Select(c => c.Name).Should().Contain("en").And.Contain("sk").And.Contain("sk-SK");
    }

    [Fact]
    public void GetAll_ReturnsAllKeys()
    {
        var provider = new JsonTranslationProvider(GetTestResourcesPath());
        var all = provider.GetAll(CultureInfo.GetCultureInfo("en"));
        all.Should().ContainKey("greeting");
        all.Should().ContainKey("errors.notFound");
        all.Should().ContainKey("errors.forbidden");
    }

    [Fact]
    public void Constructor_ThrowsForInvalidPath()
    {
        var act = () => new JsonTranslationProvider("/nonexistent/path");
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Constructor_ThrowsForNullPath()
    {
        var act = () => new JsonTranslationProvider(null!);
        act.Should().Throw<ArgumentException>();
    }
}
