using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Data.Tests;

public class DatabaseTranslationProviderTests
{
    private static TestTranslationStore CreateSeededStore()
    {
        var store = new TestTranslationStore();
        store.Seed(new[]
        {
            new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "en", Value = "Hello" },
            new TranslationModel { Guid = Guid.NewGuid(), Key = "farewell", Culture = "en", Value = "Goodbye" },
            new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "sk", Value = "Ahoj" },
            new TranslationModel { Guid = Guid.NewGuid(), Key = "farewell", Culture = "sk", Value = "Dovidenia" },
            new TranslationModel { Guid = Guid.NewGuid(), Key = "greeting", Culture = "sk-SK", Value = "Ahoj (SK)" },
        });
        return store;
    }

    [Fact]
    public void GetTranslation_ReturnsValue()
    {
        var provider = new DatabaseTranslationProvider(CreateSeededStore());
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("en")).Should().Be("Hello");
    }

    [Fact]
    public void GetTranslation_SlovakCulture()
    {
        var provider = new DatabaseTranslationProvider(CreateSeededStore());
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("sk")).Should().Be("Ahoj");
        provider.GetTranslation("farewell", CultureInfo.GetCultureInfo("sk")).Should().Be("Dovidenia");
    }

    [Fact]
    public void GetTranslation_RegionalCulture()
    {
        var provider = new DatabaseTranslationProvider(CreateSeededStore());
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("sk-SK")).Should().Be("Ahoj (SK)");
    }

    [Fact]
    public void GetTranslation_ReturnsNull_WhenKeyNotFound()
    {
        var provider = new DatabaseTranslationProvider(CreateSeededStore());
        provider.GetTranslation("nonexistent", CultureInfo.GetCultureInfo("en")).Should().BeNull();
    }

    [Fact]
    public void GetTranslation_ReturnsNull_WhenCultureNotFound()
    {
        var provider = new DatabaseTranslationProvider(CreateSeededStore());
        provider.GetTranslation("greeting", CultureInfo.GetCultureInfo("de")).Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsAllKeysForCulture()
    {
        var provider = new DatabaseTranslationProvider(CreateSeededStore());
        var all = provider.GetAll(CultureInfo.GetCultureInfo("en"));
        all.Should().HaveCount(2);
        all["greeting"].Should().Be("Hello");
        all["farewell"].Should().Be("Goodbye");
    }

    [Fact]
    public void GetAll_ReturnsEmpty_ForUnknownCulture()
    {
        var provider = new DatabaseTranslationProvider(CreateSeededStore());
        provider.GetAll(CultureInfo.GetCultureInfo("de")).Should().BeEmpty();
    }

    [Fact]
    public void GetSupportedCultures_ReturnsDistinctCultures()
    {
        var provider = new DatabaseTranslationProvider(CreateSeededStore());
        var cultures = provider.GetSupportedCultures();
        cultures.Select(c => c.Name).Should().Contain("en").And.Contain("sk").And.Contain("sk-SK");
    }

    [Fact]
    public async Task GetTranslationAsync_ReturnsValue()
    {
        var provider = new DatabaseTranslationProvider(CreateSeededStore());
        var result = await provider.GetTranslationAsync("greeting", CultureInfo.GetCultureInfo("en"));
        result.Should().Be("Hello");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllKeys()
    {
        var provider = new DatabaseTranslationProvider(CreateSeededStore());
        var all = await provider.GetAllAsync(CultureInfo.GetCultureInfo("sk"));
        all.Should().HaveCount(2);
        all["greeting"].Should().Be("Ahoj");
    }

    [Fact]
    public void Constructor_ThrowsForNullStore()
    {
        var act = () => new DatabaseTranslationProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
