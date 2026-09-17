using System.Globalization;
using System.Linq;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// SH-H015 / SH-H016 / SH-H017 / SH-H018 — the four high spec-harvest findings in
/// <c>entity-localization</c>, all of which end in the <b>base column</b> (the default-culture value)
/// being destroyed, or in a destructive statement selecting the wrong rows.
/// </summary>
/// <remarks>
/// <para>
/// The contract under test: <b>a localizable field's base column holds the default-culture value, and
/// every other culture lives in a translation row.</b> Every assertion here is therefore about
/// <i>observed state</i> — the value read back out of the inner store, or the rows that survive — and
/// never about an exception not being thrown, because all four defects are silent.
/// </para>
/// <para>
/// ⚠ The inner store is <c>InMemoryBulkStore</c>, which hands back the instance it holds rather than a
/// copy. That is not incidental: SH-H016 is only observable on such a store, and it is what lets these
/// tests can read the store's own state by going to the inner store directly.
/// </para>
/// </remarks>
public class LocalizedBaseColumnIntegrityTests
{
    private const string EnglishName = "Chair";
    private const string SlovakName = "Stolicka";

    private readonly InMemoryBulkStore<TestLocalizableModel> _entityStore = new();
    private readonly InMemoryBulkStore<EntityTranslationModel> _translationStore = new();
    private readonly TestEntityLocalizationContext _context = new()
    {
        CurrentCulture = new CultureInfo("en"),
        DefaultCulture = new CultureInfo("en"),
    };

    private readonly LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel> _bulk;
    private readonly LocalizedStoreWrapper<IStore<TestLocalizableModel>, TestLocalizableModel> _single;

    public LocalizedBaseColumnIntegrityTests()
    {
        _bulk = new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, _translationStore, _context);
        _single = new LocalizedStoreWrapper<IStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, _translationStore, _context);
    }

    private void SwitchToSlovak() => _context.CurrentCulture = new CultureInfo("sk");

    private void SwitchToEnglish() => _context.CurrentCulture = new CultureInfo("en");

    /// <summary>
    /// The value physically held by the inner store. Read straight off the inner store rather than
    /// through the wrapper, so no translation is applied and this is the base column by construction.
    /// </summary>
    private TestLocalizableModel Stored(System.Guid guid) => _entityStore.Read(guid)!;

    private string StoredName(System.Guid guid) => Stored(guid).Name;

    private string? TranslationFor(System.Guid guid, string culture, string field = "Name")
        => _translationStore.Read().FirstOrDefault(
            t => t.EntityGuid == guid && t.Culture == culture && t.FieldName == field)?.Value;

    private System.Guid SeedEnglish()
    {
        SwitchToEnglish();
        return _bulk.Create(new TestLocalizableModel
        {
            Name = EnglishName,
            Description = "A chair",
            Code = "C001",
        });
    }

    // ---------------------------------------------------------------- SH-H015

    [Fact]
    public void SH_H015_Update_on_a_non_default_culture_leaves_the_base_column_on_the_default_culture()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();

        // The caller holds the entity as a Slovak speaker just edited it.
        _bulk.Update(new TestLocalizableModel
        {
            Guid = guid,
            Name = SlovakName,
            Description = "Slovak description",
            Code = "C001",
        });

        StoredName(guid).Should().Be(EnglishName,
            "the base column carries the DEFAULT culture; the Slovak text belongs in a translation row");
        TranslationFor(guid, "sk").Should().Be(SlovakName);
    }

    [Fact]
    public void SH_H015_A_read_modify_write_cycle_does_not_destroy_the_default_culture_value()
    {
        // The loop the finding describes end to end: read under sk, edit, write back.
        var guid = SeedEnglish();
        SwitchToSlovak();

        var editing = _bulk.Read(guid)!;
        editing.Name = SlovakName;
        _bulk.Update(editing);

        SwitchToEnglish();
        _bulk.Read(guid)!.Name.Should().Be(EnglishName,
            "an edit made in Slovak must not be able to destroy the English original");
    }

    [Fact]
    public void SH_H015_The_caller_entity_is_returned_unchanged()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();

        var mine = new TestLocalizableModel { Guid = guid, Name = SlovakName, Code = "C001" };
        _bulk.Update(mine);

        mine.Name.Should().Be(SlovakName,
            "preserving the base column must not be done by mutating an object the caller owns");
    }

    [Fact]
    public void SH_H015_The_single_store_wrapper_behaves_identically()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();

        _single.Update(new TestLocalizableModel { Guid = guid, Name = SlovakName, Code = "C001" });

        StoredName(guid).Should().Be(EnglishName);
        TranslationFor(guid, "sk").Should().Be(SlovakName);
    }

    [Fact]
    public void SH_H015_The_collection_update_overload_behaves_identically()
    {
        var first = SeedEnglish();
        SwitchToEnglish();
        var second = _bulk.Create(new TestLocalizableModel { Name = "Table", Code = "T001" });
        SwitchToSlovak();

        _bulk.Update(new[]
        {
            new TestLocalizableModel { Guid = first, Name = SlovakName, Code = "C001" },
            new TestLocalizableModel { Guid = second, Name = "Stol", Code = "T001" },
        });

        StoredName(first).Should().Be(EnglishName);
        StoredName(second).Should().Be("Table");
        TranslationFor(first, "sk").Should().Be(SlovakName);
        TranslationFor(second, "sk").Should().Be("Stol");
    }

    // ---------------------------------------------------------------- SH-H016

    [Fact]
    public void SH_H016_A_localized_read_does_not_overwrite_the_values_the_store_holds()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();
        SeedSlovakNameTranslation(guid);

        var localized = _bulk.Read(guid)!;

        localized.Name.Should().Be(SlovakName, "the caller asked for Slovak");
        StoredName(guid).Should().Be(EnglishName,
            "applying a translation must not write through to the instance the store holds");
    }

    [Fact]
    public void SH_H016_A_default_culture_read_after_a_localized_one_still_returns_the_default_value()
    {
        // The consequence a consumer actually sees: one Slovak page view, and English is gone.
        var guid = SeedEnglish();
        SwitchToSlovak();
        SeedSlovakNameTranslation(guid);
        _bulk.Read(guid);

        SwitchToEnglish();
        _bulk.Read(guid)!.Name.Should().Be(EnglishName);
    }

    [Fact]
    public void SH_H016_A_localized_read_returns_a_detached_instance()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();

        var localized = _bulk.Read(guid)!;

        localized.Should().NotBeSameAs(Stored(guid),
            "a non-default-culture read returns a detached entity whether or not a translation row "
            + "exists; copying only on a hit would vary per entity inside one result set");
        localized.Code.Should().Be("C001", "the copy is faithful, not a partial re-map");
        localized.Description.Should().Be("A chair");
        localized.Guid.Should().Be(guid);
    }

    [Fact]
    public void SH_H016_The_bulk_read_paths_are_detached_too()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();
        SeedSlovakNameTranslation(guid);

        _bulk.Read().Single().Name.Should().Be(SlovakName);
        StoredName(guid).Should().Be(EnglishName);

        _bulk.Read(filter: null, orderBy: null, limit: null, offset: null).Single().Name
            .Should().Be(SlovakName);
        StoredName(guid).Should().Be(EnglishName);
    }

    [Fact]
    public void SH_H016_The_single_store_wrapper_is_detached_too()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();
        SeedSlovakNameTranslation(guid);

        _single.Read(guid)!.Name.Should().Be(SlovakName);
        StoredName(guid).Should().Be(EnglishName);
    }

    // ---------------------------------------------------------------- SH-H017

    [Fact]
    public void SH_H017_Filter_based_update_leaves_the_base_column_on_the_default_culture()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();

        _bulk.Update(x => x.Code == "C001", e => e.Name = SlovakName);

        StoredName(guid).Should().Be(EnglishName);
        TranslationFor(guid, "sk").Should().Be(SlovakName);
    }

    [Fact]
    public void SH_H017_A_non_localizable_field_changed_by_the_same_action_still_persists()
    {
        // Capture/restore covers the localizable fields only; everything else the action did must land.
        var guid = SeedEnglish();
        SwitchToSlovak();

        _bulk.Update(x => x.Code == "C001", e =>
        {
            e.Name = SlovakName;
            e.Code = "C002";
        });

        Stored(guid).Code.Should().Be("C002");
        StoredName(guid).Should().Be(EnglishName);
    }

    [Fact]
    public void SH_H017_The_PropertyUpdate_overload_reaches_the_same_path()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();

        var updates = new PropertyUpdate<TestLocalizableModel>().Set(x => x.Name, SlovakName);
        _bulk.Update(x => x.Code == "C001", updates);

        StoredName(guid).Should().Be(EnglishName);
        TranslationFor(guid, "sk").Should().Be(SlovakName);
    }

    // ---------------------------------------------------------------- SH-H018

    /// <summary>
    /// Two rows are deliberately crossed: <c>target</c> is English "Chair" carrying a Slovak
    /// translation "Stolicka", while <c>decoy</c> has "Stolicka" sitting in its <i>base</i> column.
    /// Under sk a predicate naming "Stolicka" must select <c>target</c> and never <c>decoy</c> — which
    /// is exactly what an unrewritten filter got backwards.
    /// </summary>
    private (System.Guid Target, System.Guid Decoy) SeedCrossedPair()
    {
        SwitchToEnglish();
        var target = _bulk.Create(new TestLocalizableModel { Name = EnglishName, Code = "TARGET" });
        var decoy = _bulk.Create(new TestLocalizableModel { Name = SlovakName, Code = "DECOY" });
        SeedSlovakNameTranslation(target);
        SwitchToSlovak();
        return (target, decoy);
    }

    private void SeedSlovakNameTranslation(System.Guid guid)
        => _translationStore.Create(new EntityTranslationModel
        {
            EntityGuid = guid,
            EntityType = nameof(TestLocalizableModel),
            FieldName = "Name",
            Culture = "sk",
            Value = SlovakName,
        });

    [Fact]
    public void SH_H018_Filter_based_delete_removes_the_rows_the_equivalent_read_returns()
    {
        var (target, decoy) = SeedCrossedPair();

        _bulk.Read(x => x.Name == SlovakName, null, null, null).Select(x => x.Guid)
            .Should().Equal(target);

        _bulk.Delete(x => x.Name == SlovakName);

        _entityStore.Read().Select(x => x.Guid).Should().BeEquivalentTo(
            new System.Guid?[] { decoy },
            "a destructive statement must not disagree with its own read about which rows it covers");
    }

    [Fact]
    public void SH_H018_Filter_based_update_targets_the_rows_the_equivalent_read_returns()
    {
        var (target, decoy) = SeedCrossedPair();

        _bulk.Update(x => x.Name == SlovakName, e => e.Code = "TOUCHED");

        Stored(target).Code.Should().Be("TOUCHED");
        Stored(decoy).Code.Should().Be("DECOY");
    }

    [Fact]
    public void SH_H018_A_native_PropertyUpdate_resolves_its_filter_even_when_it_touches_no_localizable_field()
    {
        var (target, decoy) = SeedCrossedPair();

        var updates = new PropertyUpdate<TestLocalizableModel>().Set(x => x.Code, "TOUCHED");
        _bulk.Update(x => x.Name == SlovakName, updates);

        Stored(target).Code.Should().Be("TOUCHED");
        Stored(decoy).Code.Should().Be("DECOY");
    }

    /// <remarks>
    /// The property this repo cares most about (CLAUDE.md § Conventions, the match-all family): routing
    /// a destructive path through <c>RewriteFilter</c> is only safe because a predicate that resolves to
    /// <b>no</b> translation becomes <c>x =&gt; false</c>, not an absent filter. Before this fix a delete
    /// never reached the rewriter at all, so nothing pinned it.
    /// </remarks>
    [Fact]
    public void A_localized_predicate_matching_no_translation_deletes_nothing_not_everything()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();

        _bulk.Delete(x => x.Name == "no-such-translation");

        _entityStore.Read(guid).Should().NotBeNull(
            "an empty match set must resolve to a predicate that matches nothing, never to no predicate");
    }

    [Fact]
    public void The_all_rows_synonym_still_passes_through_on_a_non_default_culture()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();

        // x => true names no localizable field, so the rewriter returns it untouched and the documented
        // all-rows synonym keeps working.
        _bulk.Delete(x => true);

        _entityStore.Read(guid).Should().BeNull();
    }

    // ------------------------------------------------- contract pins (these pass either way)

    [Fact]
    public void Pin_default_culture_writes_are_untouched()
    {
        var guid = SeedEnglish();

        _bulk.Update(new TestLocalizableModel { Guid = guid, Name = "Renamed", Code = "C001" });

        StoredName(guid).Should().Be("Renamed",
            "on the default culture the base column IS the value the caller supplied");
        _translationStore.Read().Should().BeEmpty();
    }

    [Fact]
    public void Pin_default_culture_reads_return_the_inner_store_instance()
    {
        var guid = SeedEnglish();

        _bulk.Read(guid).Should().BeSameAs(Stored(guid),
            "the wrapper is a pass-through on the default culture; the detach is the localized path only");
    }

    [Fact]
    public void Pin_a_filter_naming_no_localizable_field_is_unaffected()
    {
        var guid = SeedEnglish();
        SwitchToSlovak();

        _bulk.Delete(x => x.Code == "C001");

        _entityStore.Read(guid).Should().BeNull();
    }
}
