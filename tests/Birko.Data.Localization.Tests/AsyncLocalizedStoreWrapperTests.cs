using System.Globalization;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Stores;
using Birko.Configuration;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

public class AsyncLocalizedStoreWrapperTests
{
    private readonly InMemoryAsyncBulkStore<TestLocalizableModel> _entityStore;
    private readonly InMemoryAsyncBulkStore<EntityTranslationModel> _translationStore;
    private readonly TestEntityLocalizationContext _context;
    private readonly AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel> _wrapper;

    public AsyncLocalizedStoreWrapperTests()
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

    [Fact]
    public async Task ReadAsync_DefaultCulture_ReturnsOriginalValues()
    {
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        var guid = await _wrapper.CreateAsync(product);

        var result = await _wrapper.ReadAsync(guid);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Widget");
        result.Description.Should().Be("A widget");
    }

    [Fact]
    public async Task ReadAsync_NonDefaultCulture_AppliesTranslations()
    {
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        var guid = await _wrapper.CreateAsync(product);

        await _translationStore.CreateAsync(new EntityTranslationModel
        {
            EntityGuid = guid, EntityType = "TestLocalizableModel",
            FieldName = "Name", Culture = "sk", Value = "Komponent"
        });
        await _translationStore.CreateAsync(new EntityTranslationModel
        {
            EntityGuid = guid, EntityType = "TestLocalizableModel",
            FieldName = "Description", Culture = "sk", Value = "Popis"
        });

        _context.CurrentCulture = new CultureInfo("sk");

        var result = await _wrapper.ReadAsync(guid);

        result!.Name.Should().Be("Komponent");
        result.Description.Should().Be("Popis");
        result.Code.Should().Be("W001");
    }

    [Fact]
    public async Task CreateAsync_NonDefaultCulture_PersistsTranslations()
    {
        _context.CurrentCulture = new CultureInfo("de");

        var product = new TestLocalizableModel { Name = "Gerät", Description = "Beschreibung", Code = "W001" };
        await _wrapper.CreateAsync(product);

        var translations = (await _translationStore.ReadAsync(CancellationToken.None)).ToList();
        translations.Should().HaveCount(2);
        translations.Should().Contain(t => t.FieldName == "Name" && t.Value == "Gerät" && t.Culture == "de");
        translations.Should().Contain(t => t.FieldName == "Description" && t.Value == "Beschreibung" && t.Culture == "de");
    }

    [Fact]
    public async Task CreateAsync_DefaultCulture_DoesNotCreateTranslations()
    {
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        await _wrapper.CreateAsync(product);

        var translations = await _translationStore.ReadAsync(CancellationToken.None);
        translations.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_NonDefaultCulture_UpsertsTranslations()
    {
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        var guid = await _wrapper.CreateAsync(product);

        _context.CurrentCulture = new CultureInfo("sk");
        product.Name = "Komponent";
        product.Description = "Popis";
        await _wrapper.UpdateAsync(product);

        var translations = (await _translationStore.ReadAsync(CancellationToken.None)).ToList();
        translations.Should().HaveCount(2);

        product.Name = "Komponent v2";
        await _wrapper.UpdateAsync(product);

        translations = (await _translationStore.ReadAsync(CancellationToken.None)).ToList();
        translations.Should().HaveCount(2);
        translations.Should().Contain(t => t.FieldName == "Name" && t.Value == "Komponent v2");
    }

    [Fact]
    public async Task DeleteAsync_RemovesAllTranslations()
    {
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        var guid = await _wrapper.CreateAsync(product);

        _context.CurrentCulture = new CultureInfo("sk");
        product.Name = "Komponent";
        product.Description = "Popis";
        await _wrapper.UpdateAsync(product);

        (await _translationStore.ReadAsync(CancellationToken.None)).Should().HaveCount(2);

        await _wrapper.DeleteAsync(product);

        (await _translationStore.ReadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task BulkReadAsync_AppliesTranslationsToAll()
    {
        var p1 = new TestLocalizableModel { Name = "Widget", Description = "Desc1", Code = "W001" };
        var p2 = new TestLocalizableModel { Name = "Gadget", Description = "Desc2", Code = "G001" };
        var guid1 = await _wrapper.CreateAsync(p1);
        var guid2 = await _wrapper.CreateAsync(p2);

        await _translationStore.CreateAsync(new EntityTranslationModel
        {
            EntityGuid = guid1, EntityType = "TestLocalizableModel",
            FieldName = "Name", Culture = "sk", Value = "Komponent"
        });
        await _translationStore.CreateAsync(new EntityTranslationModel
        {
            EntityGuid = guid2, EntityType = "TestLocalizableModel",
            FieldName = "Name", Culture = "sk", Value = "Zariadenie"
        });

        _context.CurrentCulture = new CultureInfo("sk");

        var results = (await _wrapper.ReadAsync(CancellationToken.None)).ToList();
        results.Should().HaveCount(2);
        results.Should().Contain(p => p.Name == "Komponent");
        results.Should().Contain(p => p.Name == "Zariadenie");
    }

    [Fact]
    public void Constructor_NullInnerStore_Throws()
    {
        var act = () => new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            null!, _translationStore, _context);
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    [Fact]
    public void Constructor_NullTranslationStore_Throws()
    {
        var act = () => new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, null!, _context);
        act.Should().Throw<ArgumentNullException>().WithParameterName("translationStore");
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        var act = () => new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, _translationStore, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public async Task ReadAsync_PartialTranslation_OnlyTranslatesAvailableFields()
    {
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        var guid = await _wrapper.CreateAsync(product);

        await _translationStore.CreateAsync(new EntityTranslationModel
        {
            EntityGuid = guid, EntityType = "TestLocalizableModel",
            FieldName = "Name", Culture = "sk", Value = "Komponent"
        });

        _context.CurrentCulture = new CultureInfo("sk");

        var result = await _wrapper.ReadAsync(guid);
        result!.Name.Should().Be("Komponent");
        result.Description.Should().Be("A widget");
    }
}
