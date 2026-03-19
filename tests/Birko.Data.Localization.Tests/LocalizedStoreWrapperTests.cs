using System.Globalization;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

public class LocalizedStoreWrapperTests
{
    private readonly InMemoryBulkStore<TestLocalizableModel> _entityStore;
    private readonly InMemoryBulkStore<EntityTranslationModel> _translationStore;
    private readonly TestEntityLocalizationContext _context;
    private readonly LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel> _wrapper;

    public LocalizedStoreWrapperTests()
    {
        _entityStore = new InMemoryBulkStore<TestLocalizableModel>();
        _translationStore = new InMemoryBulkStore<EntityTranslationModel>();
        _context = new TestEntityLocalizationContext
        {
            CurrentCulture = new CultureInfo("en"),
            DefaultCulture = new CultureInfo("en")
        };
        _wrapper = new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, _translationStore, _context);
    }

    [Fact]
    public void Read_DefaultCulture_ReturnsOriginalValues()
    {
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        var guid = _wrapper.Create(product);

        var result = _wrapper.Read(guid);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Widget");
        result.Description.Should().Be("A widget");
        result.Code.Should().Be("W001");
    }

    [Fact]
    public void Read_NonDefaultCulture_AppliesTranslations()
    {
        // Create entity in default language
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        var guid = _wrapper.Create(product);

        // Add translations manually
        _translationStore.Create(new EntityTranslationModel
        {
            EntityGuid = guid,
            EntityType = "TestLocalizableModel",
            FieldName = "Name",
            Culture = "sk",
            Value = "Komponent"
        });
        _translationStore.Create(new EntityTranslationModel
        {
            EntityGuid = guid,
            EntityType = "TestLocalizableModel",
            FieldName = "Description",
            Culture = "sk",
            Value = "Komponent popis"
        });

        // Switch to Slovak
        _context.CurrentCulture = new CultureInfo("sk");

        var result = _wrapper.Read(guid);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Komponent");
        result.Description.Should().Be("Komponent popis");
        result.Code.Should().Be("W001"); // non-localizable field unchanged
    }

    [Fact]
    public void Create_NonDefaultCulture_PersistsTranslations()
    {
        _context.CurrentCulture = new CultureInfo("sk");

        var product = new TestLocalizableModel { Name = "Komponent", Description = "Popis", Code = "W001" };
        var guid = _wrapper.Create(product);

        // Verify translations were created
        var translations = _translationStore.Read();
        translations.Should().HaveCount(2);
        translations.Should().Contain(t => t.FieldName == "Name" && t.Value == "Komponent" && t.Culture == "sk");
        translations.Should().Contain(t => t.FieldName == "Description" && t.Value == "Popis" && t.Culture == "sk");
    }

    [Fact]
    public void Create_DefaultCulture_DoesNotCreateTranslations()
    {
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        _wrapper.Create(product);

        var translations = _translationStore.Read();
        translations.Should().BeEmpty();
    }

    [Fact]
    public void Update_NonDefaultCulture_UpsertsTranslations()
    {
        // Create in default culture
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        var guid = _wrapper.Create(product);

        // Switch to Slovak and update
        _context.CurrentCulture = new CultureInfo("sk");
        product.Name = "Komponent";
        product.Description = "Popis";
        _wrapper.Update(product);

        var translations = _translationStore.Read();
        translations.Should().HaveCount(2);

        // Update again — should upsert, not duplicate
        product.Name = "Komponent v2";
        _wrapper.Update(product);

        translations = _translationStore.Read().ToList();
        translations.Should().HaveCount(2);
        translations.Should().Contain(t => t.FieldName == "Name" && t.Value == "Komponent v2");
    }

    [Fact]
    public void Delete_RemovesAllTranslations()
    {
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        var guid = _wrapper.Create(product);

        // Add translations
        _context.CurrentCulture = new CultureInfo("sk");
        product.Name = "Komponent";
        product.Description = "Popis";
        _wrapper.Update(product);

        _translationStore.Read().Should().HaveCount(2);

        // Delete
        _wrapper.Delete(product);

        _translationStore.Read().Should().BeEmpty();
    }

    [Fact]
    public void BulkRead_AppliesTranslationsToAll()
    {
        var p1 = new TestLocalizableModel { Name = "Widget", Description = "Desc1", Code = "W001" };
        var p2 = new TestLocalizableModel { Name = "Gadget", Description = "Desc2", Code = "G001" };
        var guid1 = _wrapper.Create(p1);
        var guid2 = _wrapper.Create(p2);

        _translationStore.Create(new EntityTranslationModel
        {
            EntityGuid = guid1, EntityType = "TestLocalizableModel",
            FieldName = "Name", Culture = "sk", Value = "Komponent"
        });
        _translationStore.Create(new EntityTranslationModel
        {
            EntityGuid = guid2, EntityType = "TestLocalizableModel",
            FieldName = "Name", Culture = "sk", Value = "Zariadenie"
        });

        _context.CurrentCulture = new CultureInfo("sk");

        var results = _wrapper.Read().ToList();
        results.Should().HaveCount(2);
        results.Should().Contain(p => p.Name == "Komponent");
        results.Should().Contain(p => p.Name == "Zariadenie");
    }

    [Fact]
    public void Constructor_NullInnerStore_Throws()
    {
        var act = () => new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            null!, _translationStore, _context);
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    [Fact]
    public void Constructor_NullTranslationStore_Throws()
    {
        var act = () => new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, null!, _context);
        act.Should().Throw<ArgumentNullException>().WithParameterName("translationStore");
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        var act = () => new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, _translationStore, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var inner = ((IStoreWrapper)_wrapper).GetInnerStore();
        inner.Should().BeSameAs(_entityStore);
    }

    [Fact]
    public void Read_PartialTranslation_OnlyTranslatesAvailableFields()
    {
        var product = new TestLocalizableModel { Name = "Widget", Description = "A widget", Code = "W001" };
        var guid = _wrapper.Create(product);

        // Only translate Name, not Description
        _translationStore.Create(new EntityTranslationModel
        {
            EntityGuid = guid, EntityType = "TestLocalizableModel",
            FieldName = "Name", Culture = "sk", Value = "Komponent"
        });

        _context.CurrentCulture = new CultureInfo("sk");

        var result = _wrapper.Read(guid);
        result!.Name.Should().Be("Komponent");
        result.Description.Should().Be("A widget"); // falls back to default
    }
}
