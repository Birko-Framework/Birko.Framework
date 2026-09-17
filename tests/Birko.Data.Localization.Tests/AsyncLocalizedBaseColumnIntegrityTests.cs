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
/// The async half of <see cref="LocalizedBaseColumnIntegrityTests"/> — SH-H015 / SH-H016 / SH-H017 /
/// SH-H018 against <see cref="AsyncLocalizedStoreWrapper{TStore,T}"/> and
/// <see cref="AsyncLocalizedBulkStoreWrapper{TStore,T}"/>.
/// </summary>
/// <remarks>
/// This is not symmetry for its own sake. The async bulk wrapper implements
/// <c>UpdateAsync(filter, action)</c> and <c>DeleteAsync(filter)</c> by <b>reading the matching items
/// itself</b> and looping, where the sync wrapper hands a callback to the inner store — different code
/// reaching the same destruction. CLAUDE.md § TASK-245 records the trap directly: the twin you patched
/// may not be the one anything calls, so each needs its own red-verified test.
/// </remarks>
public class AsyncLocalizedBaseColumnIntegrityTests
{
    private const string EnglishName = "Chair";
    private const string SlovakName = "Stolicka";

    private readonly InMemoryAsyncBulkStore<TestLocalizableModel> _entityStore = new();
    private readonly InMemoryAsyncBulkStore<EntityTranslationModel> _translationStore = new();
    private readonly TestEntityLocalizationContext _context = new()
    {
        CurrentCulture = new CultureInfo("en"),
        DefaultCulture = new CultureInfo("en"),
    };

    private readonly AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel> _bulk;
    private readonly AsyncLocalizedStoreWrapper<IAsyncStore<TestLocalizableModel>, TestLocalizableModel> _single;

    public AsyncLocalizedBaseColumnIntegrityTests()
    {
        _bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, _translationStore, _context);
        _single = new AsyncLocalizedStoreWrapper<IAsyncStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, _translationStore, _context);
    }

    private void SwitchToSlovak() => _context.CurrentCulture = new CultureInfo("sk");

    private void SwitchToEnglish() => _context.CurrentCulture = new CultureInfo("en");

    /// <summary>Read off the inner store, so no translation is applied and this is the base column.</summary>
    private async Task<string> StoredNameAsync(System.Guid guid)
        => (await _entityStore.ReadAsync(guid))!.Name;

    private async Task<string?> TranslationForAsync(System.Guid guid, string culture, string field = "Name")
        => (await _translationStore.ReadAsync(filter: null, orderBy: null, limit: null, offset: null))
            .FirstOrDefault(
            t => t.EntityGuid == guid && t.Culture == culture && t.FieldName == field)?.Value;

    private async Task<System.Guid> SeedEnglishAsync()
    {
        SwitchToEnglish();
        return await _bulk.CreateAsync(new TestLocalizableModel
        {
            Name = EnglishName,
            Description = "A chair",
            Code = "C001",
        });
    }

    private Task SeedSlovakNameTranslationAsync(System.Guid guid)
        => _translationStore.CreateAsync(new EntityTranslationModel
        {
            EntityGuid = guid,
            EntityType = nameof(TestLocalizableModel),
            FieldName = "Name",
            Culture = "sk",
            Value = SlovakName,
        });

    // ---------------------------------------------------------------- SH-H015

    [Fact]
    public async Task SH_H015_UpdateAsync_on_a_non_default_culture_leaves_the_base_column_alone()
    {
        var guid = await SeedEnglishAsync();
        SwitchToSlovak();

        await _bulk.UpdateAsync(new TestLocalizableModel
        {
            Guid = guid,
            Name = SlovakName,
            Code = "C001",
        });

        (await StoredNameAsync(guid)).Should().Be(EnglishName);
        (await TranslationForAsync(guid, "sk")).Should().Be(SlovakName);
    }

    [Fact]
    public async Task SH_H015_The_async_single_store_wrapper_behaves_identically()
    {
        var guid = await SeedEnglishAsync();
        SwitchToSlovak();

        await _single.UpdateAsync(new TestLocalizableModel { Guid = guid, Name = SlovakName, Code = "C001" });

        (await StoredNameAsync(guid)).Should().Be(EnglishName);
        (await TranslationForAsync(guid, "sk")).Should().Be(SlovakName);
    }

    /// <remarks>
    /// Two entities, not one: the collection path fetches the whole batch's base values in a single
    /// read keyed by GUID, and a one-element batch would pass even if that read collapsed to the
    /// single-result overload or the key lookup were wrong.
    /// </remarks>
    [Fact]
    public async Task SH_H015_The_async_collection_update_overload_behaves_identically()
    {
        var first = await SeedEnglishAsync();
        SwitchToEnglish();
        var second = await _bulk.CreateAsync(new TestLocalizableModel { Name = "Table", Code = "T001" });
        SwitchToSlovak();

        await _bulk.UpdateAsync(new[]
        {
            new TestLocalizableModel { Guid = first, Name = SlovakName, Code = "C001" },
            new TestLocalizableModel { Guid = second, Name = "Stol", Code = "T001" },
        });

        (await StoredNameAsync(first)).Should().Be(EnglishName);
        (await StoredNameAsync(second)).Should().Be("Table");
        (await TranslationForAsync(first, "sk")).Should().Be(SlovakName);
        (await TranslationForAsync(second, "sk")).Should().Be("Stol");
    }

    [Fact]
    public async Task SH_H015_The_caller_entity_is_returned_unchanged()
    {
        var guid = await SeedEnglishAsync();
        SwitchToSlovak();

        var mine = new TestLocalizableModel { Guid = guid, Name = SlovakName, Code = "C001" };
        await _bulk.UpdateAsync(mine);

        mine.Name.Should().Be(SlovakName);
    }

    // ---------------------------------------------------------------- SH-H016

    [Fact]
    public async Task SH_H016_A_localized_read_does_not_overwrite_the_values_the_store_holds()
    {
        var guid = await SeedEnglishAsync();
        SwitchToSlovak();
        await SeedSlovakNameTranslationAsync(guid);

        (await _bulk.ReadAsync(guid))!.Name.Should().Be(SlovakName);
        (await StoredNameAsync(guid)).Should().Be(EnglishName);

        SwitchToEnglish();
        (await _bulk.ReadAsync(guid))!.Name.Should().Be(EnglishName);
    }

    [Fact]
    public async Task SH_H016_The_async_single_store_wrapper_is_detached_too()
    {
        var guid = await SeedEnglishAsync();
        SwitchToSlovak();
        await SeedSlovakNameTranslationAsync(guid);

        (await _single.ReadAsync(guid))!.Name.Should().Be(SlovakName);
        (await StoredNameAsync(guid)).Should().Be(EnglishName);
    }

    [Fact]
    public async Task SH_H016_The_async_bulk_read_paths_are_detached_too()
    {
        var guid = await SeedEnglishAsync();
        SwitchToSlovak();
        await SeedSlovakNameTranslationAsync(guid);

        // The read-all overload. A positional CancellationToken is needed to pick it: the zero-argument
        // ReadAsync() is CS0121-ambiguous against the filtered overload (TASK-138).
        (await _bulk.ReadAsync(System.Threading.CancellationToken.None)).Single().Name
            .Should().Be(SlovakName);
        (await StoredNameAsync(guid)).Should().Be(EnglishName);

        // ...and the filter/order/limit/offset overload, which is a separate code path.
        (await _bulk.ReadAsync(filter: null, orderBy: null, limit: null, offset: null)).Single().Name
            .Should().Be(SlovakName);
        (await StoredNameAsync(guid)).Should().Be(EnglishName);
    }

    // ---------------------------------------------------------------- SH-H017

    [Fact]
    public async Task SH_H017_Filter_based_update_leaves_the_base_column_on_the_default_culture()
    {
        var guid = await SeedEnglishAsync();
        SwitchToSlovak();

        await _bulk.UpdateAsync(x => x.Code == "C001", e => e.Name = SlovakName);

        (await StoredNameAsync(guid)).Should().Be(EnglishName);
        (await TranslationForAsync(guid, "sk")).Should().Be(SlovakName);
    }

    [Fact]
    public async Task SH_H017_A_non_localizable_field_changed_by_the_same_action_still_persists()
    {
        var guid = await SeedEnglishAsync();
        SwitchToSlovak();

        await _bulk.UpdateAsync(x => x.Code == "C001", e =>
        {
            e.Name = SlovakName;
            e.Code = "C002";
        });

        (await _entityStore.ReadAsync(guid))!.Code.Should().Be("C002");
        (await StoredNameAsync(guid)).Should().Be(EnglishName);
    }

    // ---------------------------------------------------------------- SH-H018

    /// <inheritdoc cref="LocalizedBaseColumnIntegrityTests" />
    private async Task<(System.Guid Target, System.Guid Decoy)> SeedCrossedPairAsync()
    {
        SwitchToEnglish();
        var target = await _bulk.CreateAsync(new TestLocalizableModel { Name = EnglishName, Code = "TARGET" });
        var decoy = await _bulk.CreateAsync(new TestLocalizableModel { Name = SlovakName, Code = "DECOY" });
        await SeedSlovakNameTranslationAsync(target);
        SwitchToSlovak();
        return (target, decoy);
    }

    [Fact]
    public async Task SH_H018_Filter_based_delete_removes_the_rows_the_equivalent_read_returns()
    {
        var (target, decoy) = await SeedCrossedPairAsync();

        (await _bulk.ReadAsync(x => x.Name == SlovakName, null, null, null)).Select(x => x.Guid)
            .Should().Equal(target);

        await _bulk.DeleteAsync(x => x.Name == SlovakName);

        (await _entityStore.ReadAsync(filter: null, orderBy: null, limit: null, offset: null))
            .Select(x => x.Guid).Should().BeEquivalentTo(
            new System.Guid?[] { decoy },
            "a destructive statement must not disagree with its own read about which rows it covers");
    }

    [Fact]
    public async Task SH_H018_Filter_based_update_targets_the_rows_the_equivalent_read_returns()
    {
        var (target, decoy) = await SeedCrossedPairAsync();

        await _bulk.UpdateAsync(x => x.Name == SlovakName, e => e.Code = "TOUCHED");

        (await _entityStore.ReadAsync(target))!.Code.Should().Be("TOUCHED");
        (await _entityStore.ReadAsync(decoy))!.Code.Should().Be("DECOY");
    }

    [Fact]
    public async Task SH_H018_A_native_PropertyUpdate_resolves_its_filter_even_when_it_touches_no_localizable_field()
    {
        var (target, decoy) = await SeedCrossedPairAsync();

        var updates = new PropertyUpdate<TestLocalizableModel>().Set(x => x.Code, "TOUCHED");
        await _bulk.UpdateAsync(x => x.Name == SlovakName, updates);

        (await _entityStore.ReadAsync(target))!.Code.Should().Be("TOUCHED");
        (await _entityStore.ReadAsync(decoy))!.Code.Should().Be("DECOY");
    }

    /// <inheritdoc cref="LocalizedBaseColumnIntegrityTests" />
    [Fact]
    public async Task A_localized_predicate_matching_no_translation_deletes_nothing_not_everything()
    {
        var guid = await SeedEnglishAsync();
        SwitchToSlovak();

        await _bulk.DeleteAsync(x => x.Name == "no-such-translation");

        (await _entityStore.ReadAsync(guid)).Should().NotBeNull(
            "an empty match set must resolve to a predicate that matches nothing, never to no predicate");
    }

    [Fact]
    public async Task The_all_rows_synonym_still_passes_through_on_a_non_default_culture()
    {
        var guid = await SeedEnglishAsync();
        SwitchToSlovak();

        await _bulk.DeleteAsync(x => true);

        (await _entityStore.ReadAsync(guid)).Should().BeNull();
    }

    // ------------------------------------------------- contract pins (these pass either way)

    [Fact]
    public async Task Pin_default_culture_writes_are_untouched()
    {
        var guid = await SeedEnglishAsync();

        await _bulk.UpdateAsync(new TestLocalizableModel { Guid = guid, Name = "Renamed", Code = "C001" });

        (await StoredNameAsync(guid)).Should().Be("Renamed");
        (await _translationStore.ReadAsync(filter: null, orderBy: null, limit: null, offset: null))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Pin_default_culture_reads_return_the_inner_store_instance()
    {
        var guid = await SeedEnglishAsync();

        (await _bulk.ReadAsync(guid)).Should().BeSameAs(await _entityStore.ReadAsync(guid));
    }
}
