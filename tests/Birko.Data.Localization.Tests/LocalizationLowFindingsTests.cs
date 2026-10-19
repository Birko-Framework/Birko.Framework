using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Expressions;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// TASK-194 — the low spec-harvest findings in <c>entity-localization</c> that were fixed here (SH-L078, L079,
/// L080, L081, L082, L084, L086). Slovak is the current culture and English the default unless a test says otherwise.
/// </summary>
public class LocalizationLowFindingsTests
{
    private const string SlovakName = "Stolicka";
    private const string OtherType = "SomethingElse";

    private readonly InMemoryBulkStore<TestLocalizableModel> _entityStore = new();
    private readonly InMemoryBulkStore<EntityTranslationModel> _translationStore = new();
    private readonly InMemoryAsyncBulkStore<TestLocalizableModel> _asyncEntityStore = new();
    private readonly InMemoryAsyncBulkStore<EntityTranslationModel> _asyncTranslationStore = new();
    private readonly TestEntityLocalizationContext _context = new()
    {
        CurrentCulture = new CultureInfo("sk"),
        DefaultCulture = new CultureInfo("en"),
    };

    private LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel> Bulk()
        => new(_entityStore, _translationStore, _context);

    private LocalizedStoreWrapper<IStore<TestLocalizableModel>, TestLocalizableModel> Single()
        => new(_entityStore, _translationStore, _context);

    private AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel> AsyncBulk()
        => new(_asyncEntityStore, _asyncTranslationStore, _context);

    private static EntityTranslationModel Row(Guid guid, string value, string entityType = nameof(TestLocalizableModel), string field = "Name")
        => new() { EntityGuid = guid, EntityType = entityType, FieldName = field, Culture = "sk", Value = value };

    private Guid SeedEntity(string english = "Chair", string code = "C001")
    {
        var guid = Guid.NewGuid();
        _entityStore.Create(new TestLocalizableModel { Guid = guid, Name = english, Code = code });
        return guid;
    }

    // ---------------------------------------------------------------- SH-L078

    [Fact]
    public void SH_L078_Destroy_deletes_this_types_translation_rows_and_keeps_other_types()
    {
        var guid = SeedEntity();
        _translationStore.Create(Row(guid, SlovakName));
        _translationStore.Create(Row(Guid.NewGuid(), "cudzi", OtherType));

        Bulk().Destroy();

        _translationStore.Read().Select(t => t.EntityType).Should().Equal(OtherType);
    }

    [Fact]
    public void SH_L078_The_single_store_wrapper_destroys_them_too()
    {
        _translationStore.Create(Row(SeedEntity(), SlovakName));

        Single().Destroy();

        _translationStore.Read().Should().BeEmpty();
    }

    [Fact]
    public async Task SH_L078_The_async_wrapper_destroys_them_too()
    {
        await _asyncTranslationStore.CreateAsync(Row(Guid.NewGuid(), SlovakName));
        await _asyncTranslationStore.CreateAsync(Row(Guid.NewGuid(), "cudzi", OtherType));

        await AsyncBulk().DestroyAsync();

        (await _asyncTranslationStore.ReadAsync(filter: null, orderBy: null, limit: null, offset: null))
            .Select(t => t.EntityType).Should().Equal(OtherType);
    }

    [Fact]
    public async Task SH_L078_Init_initialises_the_translation_store_too()
    {
        Bulk().Init();
        Single().Init();
        await AsyncBulk().InitAsync();

        _translationStore.InitCalls.Should().Be(2);
        _asyncTranslationStore.InitCalls.Should().Be(1);
    }

    // ---------------------------------------------------------------- SH-L079

    private sealed class TypoModel : AbstractModel, ILocalizable
    {
        public string Name { get; set; } = string.Empty;

        public IReadOnlyList<string> GetLocalizableFields() => new[] { "Nmae" };
    }

    private sealed class ReadOnlyFieldModel : AbstractModel, ILocalizable
    {
        public string Name { get; } = string.Empty;

        public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
    }

    private sealed class SubclassWithBadField : TestLocalizableModel, ILocalizable
    {
        IReadOnlyList<string> ILocalizable.GetLocalizableFields() => new[] { nameof(Name), "Missing" };
    }

    [Fact]
    public void SH_L079_A_name_with_no_property_is_refused_when_the_wrapper_is_built()
    {
        var act = () => new LocalizedBulkStoreWrapper<IBulkStore<TypoModel>, TypoModel>(
            new InMemoryBulkStore<TypoModel>(), _translationStore, _context);

        act.Should().Throw<InvalidOperationException>().WithMessage("*TypoModel*'Nmae'*");
    }

    [Fact]
    public void SH_L079_A_property_without_a_setter_is_refused_on_the_default_culture_too()
    {
        _context.CurrentCulture = new CultureInfo("en");

        var act = () => new AsyncLocalizedStoreWrapper<IAsyncStore<ReadOnlyFieldModel>, ReadOnlyFieldModel>(
            new InMemoryAsyncBulkStore<ReadOnlyFieldModel>(), _asyncTranslationStore, _context);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'Name'*");
    }

    [Theory]
    [InlineData("sk")]
    [InlineData("en")]
    public void SH_L079_An_entity_whose_own_declaration_is_bad_is_refused_before_anything_is_written(string culture)
    {
        _context.CurrentCulture = new CultureInfo(culture);
        var bulk = Bulk();

        bulk.Invoking(b => b.Create(new SubclassWithBadField { Name = SlovakName }))
            .Should().Throw<InvalidOperationException>().WithMessage("*'Missing'*");
        bulk.Invoking(b => b.Create(new TestLocalizableModel[] { new() { Name = "ok" }, new SubclassWithBadField() }))
            .Should().Throw<InvalidOperationException>();
        Single().Invoking(w => w.Update(new SubclassWithBadField { Guid = Guid.NewGuid(), Name = SlovakName }))
            .Should().Throw<InvalidOperationException>();

        _entityStore.Read().Should().BeEmpty();
        _translationStore.Read().Should().BeEmpty();
    }

    [Fact]
    public async Task SH_L079_The_async_wrappers_refuse_before_writing_too()
    {
        var act = async () => await AsyncBulk().CreateAsync(new SubclassWithBadField { Name = SlovakName });

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _asyncEntityStore.ReadAsync(filter: null, orderBy: null, limit: null, offset: null)).Should().BeEmpty();
    }

    // The hidden property has another type: that is what makes a plain GetProperty ambiguous.
    private class BaseWithName : AbstractModel, ILocalizable
    {
        public int Name { get; set; }

        public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
    }

    private sealed class HidingName : BaseWithName
    {
        public new string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void SH_L079_A_property_hidden_with_new_resolves_to_the_hiding_one()
    {
        var store = new InMemoryBulkStore<HidingName>();
        var wrapper = new LocalizedBulkStoreWrapper<IBulkStore<HidingName>, HidingName>(store, _translationStore, _context);

        var guid = wrapper.Create(new HidingName { Name = SlovakName });

        _translationStore.Read().Single(t => t.EntityGuid == guid).Value.Should().Be(SlovakName);
    }

    [Fact]
    public void SH_L078_Deleting_a_type_without_a_name_is_refused_rather_than_deleting_every_row()
    {
        _translationStore.Create(Row(Guid.NewGuid(), SlovakName));

        var act = () => LocalizedTranslationRows.DeleteType(_translationStore, "");

        act.Should().Throw<ArgumentException>();
        _translationStore.Read().Should().HaveCount(1);
    }

    // ---------------------------------------------------------------- SH-L080

    [Fact]
    public void SH_L080_A_row_of_another_entity_type_with_the_same_guid_is_not_applied()
    {
        var guid = SeedEntity();
        _translationStore.Create(Row(guid, "cudzi", OtherType));

        Bulk().Read(guid)!.Name.Should().Be("Chair");
    }

    [Fact]
    public void SH_L080_A_row_of_another_entity_type_survives_the_entitys_delete()
    {
        var guid = SeedEntity();
        _translationStore.Create(Row(guid, SlovakName));
        _translationStore.Create(Row(guid, "cudzi", OtherType));

        Bulk().Delete(_entityStore.Read(guid)!);

        _translationStore.Read().Select(t => t.EntityType).Should().Equal(OtherType);
    }

    [Fact]
    public void SH_L080_A_save_does_not_overwrite_another_entity_types_row()
    {
        var guid = SeedEntity();
        _translationStore.Create(Row(guid, "cudzi", OtherType));
        var bulk = Bulk();

        var entity = bulk.Read(guid)!;
        entity.Name = SlovakName;
        bulk.Update(entity);

        _translationStore.Read().Where(t => t.FieldName == "Name")
            .Select(t => (t.EntityType, t.Value))
            .Should().BeEquivalentTo(new[] { (OtherType, "cudzi"), (nameof(TestLocalizableModel), SlovakName) });
    }

    [Fact]
    public async Task SH_L080_The_async_wrapper_ignores_another_types_row_too()
    {
        var guid = await _asyncEntityStore.CreateAsync(new TestLocalizableModel { Name = "Chair" });
        await _asyncTranslationStore.CreateAsync(Row(guid, "cudzi", OtherType));

        (await AsyncBulk().ReadAsync(guid))!.Name.Should().Be("Chair");
    }

    // ---------------------------------------------------------------- SH-L081

    [Fact]
    public void SH_L081_An_unknown_order_by_property_in_a_later_position_is_refused_too()
    {
        SeedEntity("Bed", "B");
        SeedEntity("Armchair", "A");

        var orderBy = OrderBy<TestLocalizableModel>.By(x => x.Name).ThenByName("NoSuchProperty");
        var act = () => Bulk().Read(null, orderBy, null, null).ToList();

        act.Should().Throw<ArgumentException>().WithMessage("*NoSuchProperty*");
    }

    [Fact]
    public void SH_L081_A_property_name_in_another_case_resolves_as_on_the_untranslated_path()
    {
        SeedEntity("Bed", "B");
        SeedEntity("Armchair", "A");

        var orderBy = OrderBy<TestLocalizableModel>.ByName("Name").ThenByName("code");

        Bulk().Read(null, orderBy, null, null).Select(x => x.Code).Should().Equal("A", "B");
    }

    [Fact]
    public void SH_L081_An_unknown_property_is_refused_even_when_there_is_nothing_to_sort()
    {
        var act = () => LocalizedOrderByHelper.ApplyInMemoryOrderBy(
            new List<TestLocalizableModel>(), OrderBy<TestLocalizableModel>.ByName("NoSuchProperty"));

        act.Should().Throw<ArgumentException>();
    }

    // ---------------------------------------------------------------- SH-L082

    [Fact]
    public void SH_L082_The_declared_fields_are_read_once_not_per_call()
    {
        SeedEntity();
        var bulk = Bulk();
        var callsAfterConstruction = _entityStore.CreateInstanceCalls;

        bulk.Count(x => x.Name == SlovakName);
        bulk.Read(x => x.Name == SlovakName, OrderBy<TestLocalizableModel>.By(x => x.Name), null, null).ToList();
        bulk.Update(x => x.Code == "C001", new PropertyUpdate<TestLocalizableModel>().Set(x => x.Code, "C002"));

        callsAfterConstruction.Should().Be(1);
        _entityStore.CreateInstanceCalls.Should().Be(1);
    }

    // ---------------------------------------------------------------- SH-L084

    [Fact]
    public void SH_L084_A_value_predicate_accepts_a_null_translation()
    {
        var split = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Name.Contains("a"), new[] { nameof(TestLocalizableModel.Name) });

        split.LocalizedConditions.Single().ValuePredicate(null).Should().BeFalse();
    }

    // ---------------------------------------------------------------- SH-L086

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -1)]
    public void SH_L086_Negative_paging_on_the_in_memory_path_is_refused(int? offset, int? limit)
    {
        SeedEntity();

        var act = () => Bulk().Read(null, OrderBy<TestLocalizableModel>.By(x => x.Name), limit, offset).ToList();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SH_L086_The_async_wrapper_refuses_it_too()
    {
        await _asyncEntityStore.CreateAsync(new TestLocalizableModel { Name = "Chair" });

        var act = async () => await AsyncBulk().ReadAsync(null, OrderBy<TestLocalizableModel>.By(x => x.Name), -1, null);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
