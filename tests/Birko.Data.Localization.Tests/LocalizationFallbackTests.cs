using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// TASK-681 — a filter on a translated field matches an entity with no translation on its base value (SH-M070), and an
/// exact-value lookup selects translation rows by hash instead of reading the whole field (SH-M082).
/// </summary>
/// <remarks>
/// The fixture, on Slovak: <c>A</c> — base "Chair", Slovak "Stolicka"; <c>B</c> — base "Lavica", no Slovak name;
/// <c>C</c> — base "Stolicka", Slovak "Kreslo". So "Stolicka" must find A only (C's base value does not count, it has a
/// translation), "Lavica" must find B (its base value counts, it has none), and "Chair" nothing.
/// </remarks>
public class LocalizationFallbackTests
{
    private readonly InMemoryBulkStore<TestLocalizableModel> _entityStore = new();
    private readonly InMemoryBulkStore<EntityTranslationModel> _translationStore = new();
    private readonly TestEntityLocalizationContext _context = new()
    {
        CurrentCulture = new CultureInfo("sk"),
        DefaultCulture = new CultureInfo("en"),
    };

    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    public LocalizationFallbackTests()
    {
        _entityStore.Create(new TestLocalizableModel { Guid = _a, Name = "Chair", Code = "A" });
        _entityStore.Create(new TestLocalizableModel { Guid = _b, Name = "Lavica", Code = "B" });
        _entityStore.Create(new TestLocalizableModel { Guid = _c, Name = "Stolicka", Code = "C" });
        _translationStore.Create(Row(_a, "Stolicka"));
        _translationStore.Create(Row(_c, "Kreslo"));
    }

    private static EntityTranslationModel Row(Guid entity, string value) => new()
    {
        EntityGuid = entity, EntityType = nameof(TestLocalizableModel), FieldName = "Name", Culture = "sk", Value = value,
    };

    private LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel> Bulk()
        => new(_entityStore, _translationStore, _context);

    private IEnumerable<Guid?> Codes(Expression<Func<TestLocalizableModel, bool>> filter)
        => Bulk().Read(filter, null, null, null).Select(x => x.Guid).OrderBy(g => g == _a ? 0 : g == _b ? 1 : 2);

    [Fact]
    public void SH_M070_Equality_matches_a_translation_or_an_untranslated_base_value_never_a_translated_entitys_base()
    {
        Codes(x => x.Name == "Stolicka").Should().Equal(_a);
        Codes(x => x.Name == "Lavica").Should().Equal(_b);
        Codes(x => x.Name == "Chair").Should().BeEmpty();
    }

    [Fact]
    public void SH_M070_The_other_shapes_fall_back_too()
    {
        Codes(x => x.Name.Contains("av")).Should().Equal(_b);
        Codes(x => x.Name.StartsWith("Stol")).Should().Equal(_a);
        Codes(x => x.Name != "Stolicka").Should().Equal(_b, _c);
    }

    [Fact]
    public void SH_M070_A_remaining_condition_still_applies_to_the_fallback()
    {
        Bulk().Count(x => x.Code == "B" && x.Name == "Lavica").Should().Be(1);
        Bulk().Count(x => x.Code == "A" && x.Name == "Lavica").Should().Be(0);
    }

    [Fact]
    public void IN_over_a_list_is_translated_with_the_fallback()
    {
        var names = new List<string> { "Stolicka", "Lavica" };
        var array = new[] { "Kreslo" };

        Codes(x => names.Contains(x.Name)).Should().Equal(_a, _b);
        Codes(x => Enumerable.Contains(names, x.Name)).Should().Equal(_a, _b);
        Codes(x => array.Contains(x.Name)).Should().Equal(_c);
    }

    [Fact]
    public async Task The_async_wrapper_falls_back_too()
    {
        var entities = new InMemoryAsyncBulkStore<TestLocalizableModel>();
        var translations = new InMemoryAsyncBulkStore<EntityTranslationModel>();
        await entities.CreateAsync(new TestLocalizableModel { Guid = _a, Name = "Chair" });
        await entities.CreateAsync(new TestLocalizableModel { Guid = _b, Name = "Lavica" });
        await entities.CreateAsync(new TestLocalizableModel { Guid = _c, Name = "Stolicka" });
        await translations.CreateAsync(Row(_a, "Stolicka"));
        await translations.CreateAsync(Row(_c, "Kreslo"));
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(entities, translations, _context);

        (await bulk.ReadAsync(x => x.Name == "Stolicka", null, null, null)).Select(x => x.Guid).Should().Equal(_a);
        (await bulk.ReadAsync(x => x.Name == "Lavica", null, null, null)).Select(x => x.Guid).Should().Equal(_b);
        (await bulk.CountAsync(x => x.Name.Contains("av"))).Should().Be(1);
    }

    [Fact]
    public void An_entity_store_that_cannot_list_falls_back_through_the_whole_slice()
    {
        var single = new LocalizedStoreWrapper<IStore<TestLocalizableModel>, TestLocalizableModel>(
            new StoreOnly<TestLocalizableModel>(_entityStore), _translationStore, _context);

        single.Count(x => x.Name == "Lavica").Should().Be(1);
        single.Count(x => x.Name == "Stolicka").Should().Be(1);
        single.Read(x => x.Name == "Chair").Should().BeNull();
    }

    [Fact]
    public void The_filter_based_writes_reach_the_fallback_match_and_spare_a_translated_entitys_base()
    {
        var bulk = Bulk();

        bulk.Update(x => x.Name == "Stolicka", e => e.Code = "TOUCHED");
        bulk.Delete(x => x.Name == "Lavica");

        _entityStore.Read(_a)!.Code.Should().Be("TOUCHED");
        _entityStore.Read(_c)!.Code.Should().Be("C", "its base value is Stolicka, but it has a Slovak name of its own");
        _entityStore.Read(_b).Should().BeNull();
        _entityStore.Read().Should().HaveCount(2);
    }

    [Fact]
    public async Task The_async_filter_based_delete_reaches_the_fallback_match_only()
    {
        var entities = new InMemoryAsyncBulkStore<TestLocalizableModel>();
        var translations = new InMemoryAsyncBulkStore<EntityTranslationModel>();
        await entities.CreateAsync(new TestLocalizableModel { Guid = _b, Name = "Lavica" });
        await entities.CreateAsync(new TestLocalizableModel { Guid = _c, Name = "Lavica" });
        await translations.CreateAsync(Row(_c, "Kreslo"));
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(entities, translations, _context);

        await bulk.DeleteAsync(x => x.Name == "Lavica");

        (await entities.ReadAsync(_b)).Should().BeNull();
        (await entities.ReadAsync(_c)).Should().NotBeNull();
    }

    [Fact]
    public void An_IN_over_a_set_that_ignores_case_is_refused_rather_than_matched_two_ways()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "stolicka" };

        var act = () => Bulk().Count(x => names.Contains(x.Name));

        act.Should().Throw<NotSupportedException>().WithMessage("*Name*");
    }

    /// <summary>A consumer's model whose hash is a plain stored property: a row written around the wrappers has none.</summary>
    public sealed class StoredHashTranslation : Data.Models.AbstractModel, IEntityTranslation
    {
        public Guid EntityGuid { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string Culture { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public string ValueHash { get; set; } = string.Empty;
    }

    [Fact]
    public void A_translation_stored_without_its_hash_is_still_found_by_an_exact_value_lookup()
    {
        var translations = new InMemoryBulkStore<StoredHashTranslation>();
        translations.Create(new StoredHashTranslation
        {
            EntityGuid = _c, EntityType = nameof(TestLocalizableModel), FieldName = "Name", Culture = "sk", Value = "Kreslo",
        });
        var bulk = new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel, StoredHashTranslation>(
            _entityStore, translations, _context);

        bulk.Read(x => x.Name == "Kreslo", null, null, null).Select(x => x.Guid).Should().Equal(_c);
        bulk.Count(x => x.Name == "Stolicka").Should().Be(0, "C has a translation, so its base value does not count");
    }

    // ---------------------------------------------------------------- SH-M082

    [Fact]
    public void SH_M082_An_exact_value_lookup_reads_only_the_matching_rows_not_the_whole_field()
    {
        for (var i = 0; i < 1000; i++)
        {
            var guid = Guid.NewGuid();
            _entityStore.Create(new TestLocalizableModel { Guid = guid, Name = $"en{i:D4}" });
            _translationStore.Create(Row(guid, $"sk{i:D4}"));
        }
        var before = _translationStore.RowsRead;

        Bulk().Count(x => x.Name == "sk0500").Should().Be(1);

        // The hash lookup returns the one row; the fallback reads the candidates' translations (none: no base "sk0500").
        (_translationStore.RowsRead - before).Should().BeLessThan(10);
    }

    [Fact]
    public void ValueHash_is_computed_from_the_value_on_the_default_model()
    {
        var row = new EntityTranslationModel { Value = "Stolicka", ValueHash = "ignored" };

        row.ValueHash.Should().Be(LocalizedValueHash.Of("Stolicka")).And.HaveLength(64);
    }

    /// <summary>An <see cref="IStore{T}"/> that is not a bulk store: it cannot list, so the fallback cannot read candidates.</summary>
    private sealed class StoreOnly<T> : IStore<T> where T : Data.Models.AbstractModel
    {
        private readonly IBulkStore<T> _inner;

        public StoreOnly(IBulkStore<T> inner) => _inner = inner;

        public T? Read(Guid guid) => _inner.Read(guid);
        public T? Read(Expression<Func<T, bool>>? filter = null) => ((IReadStore<T>)_inner).Read(filter);
        public long Count(Expression<Func<T, bool>>? filter = null) => _inner.Count(filter);
        public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null) => _inner.Create(data, storeDelegate);
        public void Update(T data, StoreDataDelegate<T>? storeDelegate = null) => _inner.Update(data, storeDelegate);
        public void Delete(T data) => _inner.Delete(data);
        public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null) => _inner.Save(data, storeDelegate);
        public void Init() => _inner.Init();
        public void Destroy() => _inner.Destroy();
        public T CreateInstance() => _inner.CreateInstance();
    }
}
