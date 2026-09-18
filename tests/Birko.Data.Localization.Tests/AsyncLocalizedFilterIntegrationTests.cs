using System.Globalization;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Stores;
using Birko.Configuration;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

public class AsyncLocalizedFilterIntegrationTests
{
    private readonly InMemoryAsyncBulkStore<TestLocalizableModel> _entityStore;
    private readonly InMemoryAsyncBulkStore<EntityTranslationModel> _translationStore;
    private readonly TestEntityLocalizationContext _context;
    private readonly AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel> _wrapper;

    public AsyncLocalizedFilterIntegrationTests()
    {
        _entityStore = new InMemoryAsyncBulkStore<TestLocalizableModel>();
        _translationStore = new InMemoryAsyncBulkStore<EntityTranslationModel>();
        _context = new TestEntityLocalizationContext
        {
            CurrentCulture = new CultureInfo("en"),
            DefaultCulture = new CultureInfo("en")
        };
        _wrapper = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, _translationStore, _context);
    }

    private async Task SetupTestDataAsync()
    {
        var p1 = new TestLocalizableModel { Name = "Widget", Description = "A small widget", Code = "W001" };
        var p2 = new TestLocalizableModel { Name = "Gadget", Description = "A big gadget", Code = "G001" };
        var p3 = new TestLocalizableModel { Name = "Tool", Description = "A useful tool", Code = "T001" };
        var guid1 = await _wrapper.CreateAsync(p1);
        var guid2 = await _wrapper.CreateAsync(p2);
        var guid3 = await _wrapper.CreateAsync(p3);

        await _translationStore.CreateAsync(new EntityTranslationModel { EntityGuid = guid1, EntityType = "TestLocalizableModel", FieldName = "Name", Culture = "sk", Value = "Komponent" });
        await _translationStore.CreateAsync(new EntityTranslationModel { EntityGuid = guid1, EntityType = "TestLocalizableModel", FieldName = "Description", Culture = "sk", Value = "Malý komponent" });
        await _translationStore.CreateAsync(new EntityTranslationModel { EntityGuid = guid2, EntityType = "TestLocalizableModel", FieldName = "Name", Culture = "sk", Value = "Zariadenie" });
        await _translationStore.CreateAsync(new EntityTranslationModel { EntityGuid = guid2, EntityType = "TestLocalizableModel", FieldName = "Description", Culture = "sk", Value = "Veľké zariadenie" });
        await _translationStore.CreateAsync(new EntityTranslationModel { EntityGuid = guid3, EntityType = "TestLocalizableModel", FieldName = "Name", Culture = "sk", Value = "Nástroj" });
        await _translationStore.CreateAsync(new EntityTranslationModel { EntityGuid = guid3, EntityType = "TestLocalizableModel", FieldName = "Description", Culture = "sk", Value = "Užitočný nástroj" });
    }

    [Fact]
    public async Task ReadAsync_FilterByLocalizedName_FindsCorrectEntity()
    {
        await SetupTestDataAsync();
        _context.CurrentCulture = new CultureInfo("sk");

        var result = await ((IAsyncReadStore<TestLocalizableModel>)_wrapper).ReadAsync(x => x.Name == "Komponent");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Komponent");
        result.Code.Should().Be("W001");
    }

    [Fact]
    public async Task ReadAsync_FilterByLocalizedNameContains_FindsMatches()
    {
        await SetupTestDataAsync();
        _context.CurrentCulture = new CultureInfo("sk");

        var results = (await _wrapper.ReadAsync(x => x.Name.Contains("ar"), null, null, null, CancellationToken.None)).ToList();

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Zariadenie");
    }

    [Fact]
    public async Task ReadAsync_MixedFilter_WorksCorrectly()
    {
        await SetupTestDataAsync();
        _context.CurrentCulture = new CultureInfo("sk");

        var result = await ((IAsyncReadStore<TestLocalizableModel>)_wrapper).ReadAsync(x => x.Name == "Komponent" && x.Code == "W001");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Komponent");
    }

    [Fact]
    public async Task CountAsync_FilterByLocalizedName()
    {
        await SetupTestDataAsync();
        _context.CurrentCulture = new CultureInfo("sk");

        var count = await _wrapper.CountAsync(x => x.Name == "Komponent");

        count.Should().Be(1);
    }

    [Fact]
    public async Task BulkReadAsync_OrderByLocalizedName_SortsCorrectly()
    {
        await SetupTestDataAsync();
        _context.CurrentCulture = new CultureInfo("sk");

        var orderBy = OrderBy<TestLocalizableModel>.By(x => x.Name);
        var results = (await _wrapper.ReadAsync(orderBy: orderBy, ct: CancellationToken.None)).ToList();

        results.Should().HaveCount(3);
        results[0].Name.Should().Be("Komponent");
        results[1].Name.Should().Be("Nástroj");
        results[2].Name.Should().Be("Zariadenie");
    }

    [Fact]
    public async Task BulkReadAsync_OrderByLocalizedName_WithPagination()
    {
        await SetupTestDataAsync();
        _context.CurrentCulture = new CultureInfo("sk");

        var orderBy = OrderBy<TestLocalizableModel>.By(x => x.Name);
        var results = (await _wrapper.ReadAsync(orderBy: orderBy, limit: 2, offset: 1, ct: CancellationToken.None)).ToList();

        results.Should().HaveCount(2);
        results[0].Name.Should().Be("Nástroj");
        results[1].Name.Should().Be("Zariadenie");
    }

    [Fact]
    public async Task BulkReadAsync_FilterAndOrderByLocalized()
    {
        await SetupTestDataAsync();
        _context.CurrentCulture = new CultureInfo("sk");

        var orderBy = OrderBy<TestLocalizableModel>.ByDescending(x => x.Name);
        var results = (await _wrapper.ReadAsync(
            x => x.Description.Contains("enie"),
            orderBy, ct: CancellationToken.None)).ToList();

        // "Veľké zariadenie" and "Malý komponent" — wait, only "zariadenie" contains "enie"
        // Actually: "Veľké zariadenie" contains "enie", "Malý komponent" does not
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Zariadenie");
    }
}
