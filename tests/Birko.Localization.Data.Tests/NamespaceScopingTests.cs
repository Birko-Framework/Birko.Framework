using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Data.Tests;

public class NamespaceScopingTests
{
    private static TestTranslationStore CreateNamespacedStore()
    {
        var store = new TestTranslationStore();
        store.Seed(new[]
        {
            new TranslationModel { Guid = Guid.NewGuid(), Key = "title", Culture = "en", Value = "Orders", Namespace = "orders" },
            new TranslationModel { Guid = Guid.NewGuid(), Key = "title", Culture = "en", Value = "Settings", Namespace = "settings" },
            new TranslationModel { Guid = Guid.NewGuid(), Key = "title", Culture = "sk", Value = "Objednávky", Namespace = "orders" },
            new TranslationModel { Guid = Guid.NewGuid(), Key = "save", Culture = "en", Value = "Save", Namespace = "settings" },
        });
        return store;
    }

    [Fact]
    public void NamespaceScoping_OnlyReturnsMatchingNamespace()
    {
        var store = CreateNamespacedStore();
        var ordersProvider = new DatabaseTranslationProvider(store, @namespace: "orders");

        ordersProvider.GetTranslation("title", CultureInfo.GetCultureInfo("en")).Should().Be("Orders");
        ordersProvider.GetTranslation("save", CultureInfo.GetCultureInfo("en")).Should().BeNull();
    }

    [Fact]
    public void DifferentNamespace_ReturnsDifferentValue()
    {
        var store = CreateNamespacedStore();
        var ordersProvider = new DatabaseTranslationProvider(store, @namespace: "orders");
        var settingsProvider = new DatabaseTranslationProvider(store, @namespace: "settings");

        ordersProvider.GetTranslation("title", CultureInfo.GetCultureInfo("en")).Should().Be("Orders");
        settingsProvider.GetTranslation("title", CultureInfo.GetCultureInfo("en")).Should().Be("Settings");
    }

    [Fact]
    public void NoNamespace_ReturnsAllTranslations()
    {
        var store = CreateNamespacedStore();
        var provider = new DatabaseTranslationProvider(store);

        var all = provider.GetAll(CultureInfo.GetCultureInfo("en"));
        // "title" appears in both namespaces, last write wins in dict → 2 unique keys
        all.Should().HaveCount(2);
        all.Should().ContainKey("title");
        all.Should().ContainKey("save");
    }

    [Fact]
    public void GetSupportedCultures_WithNamespace_OnlyReturnsNamespacedCultures()
    {
        var store = CreateNamespacedStore();
        var ordersProvider = new DatabaseTranslationProvider(store, @namespace: "orders");
        var cultures = ordersProvider.GetSupportedCultures();
        cultures.Select(c => c.Name).Should().Contain("en").And.Contain("sk");
    }
}
