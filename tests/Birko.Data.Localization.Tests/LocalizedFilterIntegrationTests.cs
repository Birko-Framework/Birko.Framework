using System.Globalization;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

public class LocalizedFilterIntegrationTests
{
    private readonly InMemoryBulkStore<TestLocalizableModel> _entityStore;
    private readonly InMemoryBulkStore<EntityTranslationModel> _translationStore;
    private readonly TestEntityLocalizationContext _context;
    private readonly LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel> _wrapper;

    public LocalizedFilterIntegrationTests()
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

    private void SetupTestData()
    {
        var p1 = new TestLocalizableModel { Name = "Widget", Description = "A small widget", Code = "W001" };
        var p2 = new TestLocalizableModel { Name = "Gadget", Description = "A big gadget", Code = "G001" };
        var p3 = new TestLocalizableModel { Name = "Tool", Description = "A useful tool", Code = "T001" };
        var guid1 = _wrapper.Create(p1);
        var guid2 = _wrapper.Create(p2);
        var guid3 = _wrapper.Create(p3);

        // Slovak translations
        _translationStore.Create(new EntityTranslationModel { EntityGuid = guid1, EntityType = "TestLocalizableModel", FieldName = "Name", Culture = "sk", Value = "Komponent" });
        _translationStore.Create(new EntityTranslationModel { EntityGuid = guid1, EntityType = "TestLocalizableModel", FieldName = "Description", Culture = "sk", Value = "Malý komponent" });
        _translationStore.Create(new EntityTranslationModel { EntityGuid = guid2, EntityType = "TestLocalizableModel", FieldName = "Name", Culture = "sk", Value = "Zariadenie" });
        _translationStore.Create(new EntityTranslationModel { EntityGuid = guid2, EntityType = "TestLocalizableModel", FieldName = "Description", Culture = "sk", Value = "Veľké zariadenie" });
        _translationStore.Create(new EntityTranslationModel { EntityGuid = guid3, EntityType = "TestLocalizableModel", FieldName = "Name", Culture = "sk", Value = "Nástroj" });
        _translationStore.Create(new EntityTranslationModel { EntityGuid = guid3, EntityType = "TestLocalizableModel", FieldName = "Description", Culture = "sk", Value = "Užitočný nástroj" });
    }

    [Fact]
    public void Read_FilterByLocalizedName_FindsCorrectEntity()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var result = _wrapper.Read(x => x.Name == "Komponent");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Komponent");
        result.Code.Should().Be("W001");
    }

    [Fact]
    public void Read_FilterByLocalizedNameContains_FindsMatchingEntities()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var results = _wrapper.Read(x => x.Name.Contains("ar"), null, null, null).ToList();

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Zariadenie");
    }

    [Fact]
    public void Read_FilterByLocalizedAndNonLocalized_CombinesCorrectly()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var result = _wrapper.Read(x => x.Name == "Komponent" && x.Code == "W001");

        result.Should().NotBeNull();
        result!.Code.Should().Be("W001");
        result.Name.Should().Be("Komponent");
    }

    [Fact]
    public void Read_FilterByLocalizedAndNonLocalized_NoMatch()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        // Komponent has Code W001, not G001
        var result = _wrapper.Read(x => x.Name == "Komponent" && x.Code == "G001");

        result.Should().BeNull();
    }

    [Fact]
    public void Count_FilterByLocalizedName_CountsCorrectly()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var count = _wrapper.Count(x => x.Name == "Komponent");

        count.Should().Be(1);
    }

    [Fact]
    public void Count_FilterByLocalizedNameContains_CountsAll()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var count = _wrapper.Count(x => x.Description.Contains("komponent"));

        count.Should().Be(1);
    }

    [Fact]
    public void BulkRead_FilterByLocalizedField_WithPagination()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        // Get all 3, paginate: skip 1, take 1
        var results = _wrapper.Read(null, null, 1, 1).ToList();

        results.Should().HaveCount(1);
    }

    [Fact]
    public void BulkRead_OrderByLocalizedName_SortsCorrectly()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var orderBy = OrderBy<TestLocalizableModel>.By(x => x.Name);
        var results = _wrapper.Read(null, orderBy, null, null).ToList();

        results.Should().HaveCount(3);
        // Alphabetical in Slovak: Komponent, Nástroj, Zariadenie
        results[0].Name.Should().Be("Komponent");
        results[1].Name.Should().Be("Nástroj");
        results[2].Name.Should().Be("Zariadenie");
    }

    [Fact]
    public void BulkRead_OrderByLocalizedNameDescending_SortsCorrectly()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var orderBy = OrderBy<TestLocalizableModel>.ByDescending(x => x.Name);
        var results = _wrapper.Read(null, orderBy, null, null).ToList();

        results.Should().HaveCount(3);
        results[0].Name.Should().Be("Zariadenie");
        results[1].Name.Should().Be("Nástroj");
        results[2].Name.Should().Be("Komponent");
    }

    [Fact]
    public void BulkRead_OrderByLocalizedName_WithPagination()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var orderBy = OrderBy<TestLocalizableModel>.By(x => x.Name);
        var results = _wrapper.Read(null, orderBy, 2, 0).ToList();

        results.Should().HaveCount(2);
        results[0].Name.Should().Be("Komponent");
        results[1].Name.Should().Be("Nástroj");
    }

    [Fact]
    public void BulkRead_OrderByLocalizedName_WithOffset()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var orderBy = OrderBy<TestLocalizableModel>.By(x => x.Name);
        var results = _wrapper.Read(null, orderBy, 2, 1).ToList();

        results.Should().HaveCount(2);
        results[0].Name.Should().Be("Nástroj");
        results[1].Name.Should().Be("Zariadenie");
    }

    [Fact]
    public void BulkRead_OrderByNonLocalizedField_DelegatedToStore()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var orderBy = OrderBy<TestLocalizableModel>.By(x => x.Code);
        var results = _wrapper.Read(null, orderBy, null, null).ToList();

        results.Should().HaveCount(3);
        // All names should be translated regardless of ordering
        results.Should().Contain(r => r.Name == "Komponent");
        results.Should().Contain(r => r.Name == "Zariadenie");
        results.Should().Contain(r => r.Name == "Nástroj");
    }

    [Fact]
    public void Read_DefaultCulture_FilterStillWorks()
    {
        SetupTestData();
        // Default culture — no translation rewriting
        var result = _wrapper.Read(x => x.Name == "Widget");

        result.Should().NotBeNull();
        result!.Code.Should().Be("W001");
    }

    [Fact]
    public void Read_FilterByLocalizedName_NoTranslationExists_ReturnsEmpty()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var result = _wrapper.Read(x => x.Name == "NonExistent");

        result.Should().BeNull();
    }

    [Fact]
    public void Read_FilterByLocalizedStartsWith()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var results = _wrapper.Read(x => x.Name.StartsWith("Komp"), null, null, null).ToList();

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Komponent");
    }

    [Fact]
    public void Read_FilterByLocalizedEndsWith()
    {
        SetupTestData();
        _context.CurrentCulture = new CultureInfo("sk");

        var results = _wrapper.Read(x => x.Name.EndsWith("enie"), null, null, null).ToList();

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Zariadenie");
    }
}
