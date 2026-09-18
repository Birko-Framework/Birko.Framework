using System;
using System.IO;
using System.Linq;
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
    public void GetSupportedCultures_SkipsEmptyAndInvalidFileNames()
    {
        // CR-L273: a bare ".json" (empty base name) is skipped by the Where filter; the removed dead
        // ternary never mapped it to InvariantCulture. Valid culture files are still returned.
        var dir = Path.Combine(Path.GetTempPath(), "birko-loc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "en.json"), "{}");
            File.WriteAllText(Path.Combine(dir, ".json"), "{}");

            var provider = new JsonTranslationProvider(dir);
            var cultures = provider.GetSupportedCultures();

            cultures.Select(c => c.Name).Should().Contain("en");
            cultures.Should().NotContain(c => c.Equals(CultureInfo.InvariantCulture));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
