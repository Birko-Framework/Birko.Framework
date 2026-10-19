using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// TASK-168 — the medium spec-harvest findings in <c>entity-localization</c> that were fixed here
/// (SH-M071, M072, M073, M075, M076, M077, M078, M079, M080), plus the filter-update defect found while
/// verifying M075. Every test runs under Slovak with English as the default culture, because every one
/// of these defects is invisible on the default culture.
/// </summary>
public class LocalizationMediumFindingsTests
{
    private const string EnglishName = "Chair";
    private const string SlovakName = "Stolicka";

    private readonly InMemoryBulkStore<TestLocalizableModel> _entityStore = new();
    private readonly InMemoryBulkStore<EntityTranslationModel> _translationStore = new();
    private readonly InMemoryAsyncBulkStore<TestLocalizableModel> _asyncEntityStore = new();
    private readonly InMemoryAsyncBulkStore<EntityTranslationModel> _asyncTranslationStore = new();
    private readonly TestEntityLocalizationContext _context = new()
    {
        CurrentCulture = new CultureInfo("en"),
        DefaultCulture = new CultureInfo("en"),
    };

    private readonly LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel> _bulk;
    private readonly LocalizedStoreWrapper<IStore<TestLocalizableModel>, TestLocalizableModel> _single;
    private readonly AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel> _asyncBulk;
    private readonly AsyncLocalizedStoreWrapper<IAsyncStore<TestLocalizableModel>, TestLocalizableModel> _asyncSingle;

    public LocalizationMediumFindingsTests()
    {
        _bulk = new(_entityStore, _translationStore, _context);
        _single = new(_entityStore, _translationStore, _context);
        _asyncBulk = new(_asyncEntityStore, _asyncTranslationStore, _context);
        _asyncSingle = new(_asyncEntityStore, _asyncTranslationStore, _context);
    }

    private void SwitchToSlovak() => _context.CurrentCulture = new CultureInfo("sk");

    private void SwitchToEnglish() => _context.CurrentCulture = new CultureInfo("en");

    private EntityTranslationModel[] Rows(Guid guid, string field = "Name")
        => _translationStore.Read().Where(t => t.EntityGuid == guid && t.FieldName == field && t.Culture == "sk").ToArray();

    private async Task<EntityTranslationModel[]> AsyncRowsAsync(Guid guid, string field = "Name")
        => (await _asyncTranslationStore.ReadAsync(filter: null, orderBy: null, limit: null, offset: null))
            .Where(t => t.EntityGuid == guid && t.FieldName == field && t.Culture == "sk").ToArray();

    private static EntityTranslationModel SlovakRow(Guid guid, string value) => new()
    {
        EntityGuid = guid,
        EntityType = nameof(TestLocalizableModel),
        FieldName = "Name",
        Culture = "sk",
        Value = value,
    };

    private Guid SeedWithSlovakName(string english = EnglishName, string slovak = SlovakName, string code = "C001")
    {
        SwitchToEnglish();
        var guid = _bulk.Create(new TestLocalizableModel { Name = english, Description = "desc", Code = code });
        _translationStore.Create(SlovakRow(guid, slovak));
        SwitchToSlovak();
        return guid;
    }

    private async Task<Guid> AsyncSeedWithSlovakNameAsync(string code = "C001")
    {
        SwitchToEnglish();
        var guid = await _asyncBulk.CreateAsync(new TestLocalizableModel { Name = EnglishName, Description = "desc", Code = code });
        await _asyncTranslationStore.CreateAsync(SlovakRow(guid, SlovakName));
        SwitchToSlovak();
        return guid;
    }

    // ---------------------------------------------------------------- SH-M071

    [Fact]
    public void SH_M071_An_unknown_first_order_by_field_is_skipped_not_a_crash()
    {
        SeedWithSlovakName("Bed", "Postel", "B");
        SeedWithSlovakName("Armchair", "Kreslo", "A");

        var orderBy = OrderBy<TestLocalizableModel>.ByName("NoSuchProperty").ThenBy(x => x.Name);
        var act = () => _bulk.Read(null, orderBy, null, null).Select(x => x.Name).ToList();

        act.Should().NotThrow();
        act().Should().Equal("Kreslo", "Postel");
    }

    // ---------------------------------------------------------------- SH-M080

    [Fact]
    public void SH_M080_Localized_ordering_collates_with_the_localization_culture_not_the_thread_culture()
    {
        // Slovak sorts "č" as its own letter after "c"; invariant and ordinal comparison put "čaj" before "cesta".
        SeedWithSlovakName("Tea", "čaj", "T");
        SeedWithSlovakName("Road", "cesta", "R");
        SeedWithSlovakName("House", "dom", "H");

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            _bulk.Read(null, OrderBy<TestLocalizableModel>.By(x => x.Name), null, null)
                .Select(x => x.Name).Should().Equal("cesta", "čaj", "dom");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---------------------------------------------------------------- SH-M077 / SH-M079

    [Fact]
    public void SH_M077_A_localized_condition_inside_an_or_is_refused_not_run_against_the_base_column()
    {
        SeedWithSlovakName();

        var act = () => _bulk.Read(x => x.Name == SlovakName || x.Code == "none", null, null, null).ToList();

        act.Should().Throw<NotSupportedException>().WithMessage("*Name*");
    }

    [Fact]
    public void SH_M077_A_negated_localized_condition_is_refused()
    {
        SeedWithSlovakName();

        var act = () => _bulk.Count(x => !(x.Name == SlovakName));

        act.Should().Throw<NotSupportedException>().WithMessage("*Name*");
    }

    [Fact]
    public void SH_M077_A_chained_call_on_a_localized_field_is_refused()
    {
        SeedWithSlovakName();

        var act = () => _bulk.Read(x => x.Name.ToLower().Contains("stol"), null, null, null).ToList();

        act.Should().Throw<NotSupportedException>().WithMessage("*Name*");
    }

    [Fact]
    public async Task SH_M077_The_async_wrapper_refuses_too()
    {
        await AsyncSeedWithSlovakNameAsync();

        var act = async () => await _asyncBulk.ReadAsync(x => x.Name == SlovakName || x.Code == "none", null, null, null);

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*Name*");
    }

    [Fact]
    public void SH_M077_The_documented_null_comparison_still_passes_through()
    {
        SeedWithSlovakName();

        _bulk.Count(x => x.Name != null).Should().Be(1);
    }

    [Fact]
    public void SH_M077_The_default_culture_is_untouched()
    {
        SeedWithSlovakName();
        SwitchToEnglish();

        _bulk.Count(x => x.Name == EnglishName || x.Code == "none").Should().Be(1);
    }

    private sealed class FailsOnce
    {
        private int _calls;

        public string Value() => _calls++ == 0 ? throw new InvalidOperationException("operand failed") : SlovakName;
    }

    [Fact]
    public void SH_M079_An_operand_that_fails_to_evaluate_is_reported_not_swallowed()
    {
        // Fails only on the analyzer's evaluation. Swallowed, the condition fell through to the inner store,
        // whose own evaluation succeeded and compared "Stolicka" with the English base column: zero rows, no error.
        SeedWithSlovakName();
        var operand = new FailsOnce();

        var act = () => _bulk.Read(x => x.Name == operand.Value(), null, null, null).ToList();

        act.Should().Throw<InvalidOperationException>().WithMessage("operand failed");
    }

    [Fact]
    public void SH_M079_Comparing_a_localized_field_with_another_column_is_refused()
    {
        SeedWithSlovakName();

        var act = () => _bulk.Read(x => x.Name == x.Code, null, null, null).ToList();

        act.Should().Throw<NotSupportedException>().WithMessage("*Name*");
    }

    [Fact]
    public void A_value_computed_through_a_nested_lambda_is_still_extracted()
    {
        // The nested lambda's own parameter is not the entity: the value is a constant and must be translated.
        var guid = SeedWithSlovakName();
        var names = new[] { "Other", SlovakName };

        _bulk.Read(x => x.Name == names.First(n => n.StartsWith("S")), null, null, null)
            .Select(x => x.Guid).Should().Equal(guid);
        _bulk.Count(x => x.Name.StartsWith(names.First(n => n.Length == 8))).Should().Be(1);
    }

    // ---------------------------------------------------------------- SH-M078

    [Fact]
    public void SH_M078_A_case_insensitive_contains_matches_the_translation_case_insensitively()
    {
        var guid = SeedWithSlovakName();

        _bulk.Read(x => x.Name.Contains("STOL", StringComparison.OrdinalIgnoreCase), null, null, null)
            .Select(x => x.Guid).Should().Equal(guid);
        _bulk.Count(x => x.Name.StartsWith("stol", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
        _bulk.Count(x => x.Name.EndsWith("ICKA", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    [Fact]
    public void SH_M078_A_single_argument_StartsWith_keeps_the_culture_sensitive_dotnet_default()
    {
        // "Å" composed (U+00C5) against "A" + combining ring (U+030A): equal under culture comparison, not ordinally.
        var guid = SeedWithSlovakName(slovak: "Ångström");

        _bulk.Read(x => x.Name.StartsWith("Ångs"), null, null, null).Select(x => x.Guid).Should().Equal(guid);
    }

    [Fact]
    public void SH_M077_A_localized_field_read_through_a_cast_is_refused_too()
    {
        SeedWithSlovakName();

        var act = () => _bulk.Count(x => ((TestLocalizableModel)(object)x).Name == SlovakName || x.Code == "none");

        act.Should().Throw<NotSupportedException>().WithMessage("*Name*");
    }

    [Fact]
    public void SH_M078_An_ordinal_contains_stays_case_sensitive()
    {
        SeedWithSlovakName();

        _bulk.Count(x => x.Name.Contains("STOL", StringComparison.Ordinal)).Should().Be(0);
    }

    // ---------------------------------------------------------------- SH-M073

    [Fact]
    public void SH_M073_Setting_a_field_to_null_removes_its_translation_and_the_read_falls_back()
    {
        var guid = SeedWithSlovakName();

        var entity = _bulk.Read(guid)!;
        entity.Name = null!;
        _bulk.Update(entity);

        Rows(guid).Should().BeEmpty();
        _bulk.Read(guid)!.Name.Should().Be(EnglishName);
    }

    [Fact]
    public void SH_M073_The_single_store_wrapper_removes_it_too()
    {
        var guid = SeedWithSlovakName();

        var entity = _single.Read(guid)!;
        entity.Name = null!;
        _single.Update(entity);

        Rows(guid).Should().BeEmpty();
    }

    [Fact]
    public async Task SH_M073_The_async_wrapper_removes_it_too()
    {
        var guid = await AsyncSeedWithSlovakNameAsync();

        var entity = (await _asyncBulk.ReadAsync(guid))!;
        entity.Name = null!;
        await _asyncBulk.UpdateAsync(entity);

        (await AsyncRowsAsync(guid)).Should().BeEmpty();
        (await _asyncBulk.ReadAsync(guid))!.Name.Should().Be(EnglishName);
    }

    // ---------------------------------------------------------------- SH-M076

    [Fact]
    public void SH_M076_Translations_are_keyed_by_the_guid_the_inner_store_returned()
    {
        _entityStore.AssignsGuidOnCreate = false;
        SwitchToSlovak();

        var guid = _bulk.Create(new TestLocalizableModel { Name = SlovakName });

        Rows(guid).Select(t => t.Value).Should().Equal(SlovakName);
    }

    [Fact]
    public void SH_M076_The_single_store_wrapper_keys_them_too()
    {
        _entityStore.AssignsGuidOnCreate = false;
        SwitchToSlovak();

        var guid = _single.Create(new TestLocalizableModel { Name = SlovakName });

        Rows(guid).Select(t => t.Value).Should().Equal(SlovakName);
    }

    [Fact]
    public async Task SH_M076_Async_save_returns_and_keys_by_the_created_guid()
    {
        _asyncEntityStore.AssignsGuidOnCreate = false;
        SwitchToSlovak();

        var guid = await _asyncSingle.SaveAsync(new TestLocalizableModel { Name = SlovakName });
        var bulkGuid = await _asyncBulk.SaveAsync(new TestLocalizableModel { Name = "Stol" });

        guid.Should().NotBe(Guid.Empty);
        (await AsyncRowsAsync(guid)).Select(t => t.Value).Should().Equal(SlovakName);
        (await AsyncRowsAsync(bulkGuid)).Select(t => t.Value).Should().Equal("Stol");
    }

    // ---------------------------------------------------------------- SH-M072

    private sealed class DerivedLocalizableModel : TestLocalizableModel
    {
    }

    [Fact]
    public void SH_M072_A_subclass_instance_is_found_by_a_localized_filter_on_the_base_type_store()
    {
        SwitchToSlovak();
        var guid = _bulk.Create(new DerivedLocalizableModel { Name = SlovakName, Code = "D" });

        _bulk.Read(x => x.Name == SlovakName, null, null, null).Select(x => x.Guid).Should().Equal(guid);
        Rows(guid).Single().EntityType.Should().Be(nameof(TestLocalizableModel));
    }

    [Fact]
    public async Task SH_M072_The_async_wrapper_stamps_the_store_type_too()
    {
        SwitchToSlovak();
        var guid = await _asyncBulk.CreateAsync(new DerivedLocalizableModel { Name = SlovakName, Code = "D" });

        (await _asyncBulk.CountAsync(x => x.Name == SlovakName)).Should().Be(1);
        (await AsyncRowsAsync(guid)).Single().EntityType.Should().Be(nameof(TestLocalizableModel));
    }

    // ---------------------------------------------------------------- SH-M075

    [Fact]
    public void SH_M075_A_failed_filter_update_leaves_no_translation_behind()
    {
        SwitchToEnglish();
        var guid = _bulk.Create(new TestLocalizableModel { Name = EnglishName, Code = "C001" });
        SwitchToSlovak();
        _entityStore.BeforeUpdate = _ => throw new InvalidOperationException("inner write failed");

        var act = () => _bulk.Update(x => x.Code == "C001", e => e.Name = SlovakName);

        act.Should().Throw<InvalidOperationException>();
        Rows(guid).Should().BeEmpty();
    }

    [Fact]
    public async Task SH_M075_A_failed_async_filter_update_leaves_no_translation_behind()
    {
        SwitchToEnglish();
        var guid = await _asyncBulk.CreateAsync(new TestLocalizableModel { Name = EnglishName, Code = "C001" });
        SwitchToSlovak();
        _asyncEntityStore.BeforeUpdate = _ => throw new InvalidOperationException("inner write failed");

        var act = async () => await _asyncBulk.UpdateAsync(x => x.Code == "C001", e => e.Name = SlovakName);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await AsyncRowsAsync(guid)).Should().BeEmpty();
    }

    [Fact]
    public void SH_M075_A_successful_filter_update_still_writes_the_translation_and_keeps_the_base_column()
    {
        SwitchToEnglish();
        var guid = _bulk.Create(new TestLocalizableModel { Name = EnglishName, Code = "C001" });
        SwitchToSlovak();

        _bulk.Update(x => x.Code == "C001", e => e.Name = SlovakName);

        Rows(guid).Select(t => t.Value).Should().Equal(SlovakName);
        _entityStore.Read(guid)!.Name.Should().Be(EnglishName);
    }

    // ------------------------------------------- filter update clobbering untouched translations

    [Fact]
    public void A_filter_update_that_does_not_touch_a_localized_field_keeps_its_translation()
    {
        var guid = SeedWithSlovakName();

        _bulk.Update(x => x.Code == "C001", e => e.Code = "C002");

        Rows(guid).Select(t => t.Value).Should().Equal(SlovakName);
        Rows(guid, "Description").Should().BeEmpty();
        _bulk.Read(guid)!.Name.Should().Be(SlovakName);
    }

    [Fact]
    public async Task An_async_filter_update_that_does_not_touch_a_localized_field_keeps_its_translation()
    {
        var guid = await AsyncSeedWithSlovakNameAsync();

        await _asyncBulk.UpdateAsync(x => x.Code == "C001", e => e.Code = "C002");

        (await AsyncRowsAsync(guid)).Select(t => t.Value).Should().Equal(SlovakName);
        (await AsyncRowsAsync(guid, "Description")).Should().BeEmpty();
    }

    [Fact]
    public void A_filter_update_action_sees_the_translated_value()
    {
        var guid = SeedWithSlovakName();
        string? seen = null;

        _bulk.Update(x => x.Code == "C001", e => seen = e.Name);

        seen.Should().Be(SlovakName);
        Rows(guid).Select(t => t.Value).Should().Equal(SlovakName);
    }
}
