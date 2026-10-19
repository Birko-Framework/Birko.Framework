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
/// TASK-679 — a localized list read costs one translation read per <see cref="LocalizedTranslationRows.ReadBatchSize"/>
/// entities, not one per entity. Each test counts the translation store's filtered reads and checks the values.
/// </summary>
public class LocalizationBatchedReadTests
{
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

    private AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel> AsyncBulk()
        => new(_asyncEntityStore, _asyncTranslationStore, _context);

    private static EntityTranslationModel Row(Guid guid, string value) => new()
    {
        EntityGuid = guid,
        EntityType = nameof(TestLocalizableModel),
        FieldName = nameof(TestLocalizableModel.Name),
        Culture = "sk",
        Value = value,
    };

    /// <summary>Seeds <paramref name="count"/> entities; every second one gets a Slovak name.</summary>
    private void Seed(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var guid = Guid.NewGuid();
            _entityStore.Create(new TestLocalizableModel { Guid = guid, Name = $"en{i:D4}", Code = $"C{i:D4}" });
            if (i % 2 == 0)
            {
                _translationStore.Create(Row(guid, $"sk{i:D4}"));
            }
        }
    }

    [Fact]
    public void A_list_of_three_costs_one_translation_read_and_untranslated_entities_keep_their_base_values()
    {
        Seed(3);
        var bulk = Bulk();

        var names = bulk.Read(x => x.Code != "", null, null, null).Select(x => x.Name).OrderBy(n => n).ToList();

        _translationStore.FilteredReadCalls.Should().Be(1);
        names.Should().Equal("en0001", "sk0000", "sk0002");
    }

    [Fact]
    public void Reading_everything_costs_one_translation_read_too()
    {
        Seed(3);

        Bulk().Read().Should().HaveCount(3);

        _translationStore.FilteredReadCalls.Should().Be(1);
    }

    [Fact]
    public void An_empty_list_costs_no_translation_read()
    {
        Bulk().Read(x => x.Code == "none", null, null, null).Should().BeEmpty();

        _translationStore.FilteredReadCalls.Should().Be(0);
    }

    [Fact]
    public void The_default_culture_costs_no_translation_read()
    {
        Seed(3);
        _context.CurrentCulture = new CultureInfo("en");

        Bulk().Read(x => x.Code != "", null, null, null).Should().HaveCount(3);

        _translationStore.FilteredReadCalls.Should().Be(0);
    }

    [Fact]
    public void The_in_memory_sort_path_costs_one_translation_read()
    {
        Seed(4);

        var page = Bulk().Read(null, OrderBy<TestLocalizableModel>.By(x => x.Name), 2, 0).Select(x => x.Name).ToList();

        _translationStore.FilteredReadCalls.Should().Be(1);
        page.Should().Equal("en0001", "en0003");
    }

    [Fact]
    public void A_list_larger_than_one_batch_costs_one_read_per_batch()
    {
        Seed(LocalizedTranslationRows.ReadBatchSize * 2 + 1);

        var list = Bulk().Read().ToList();

        _translationStore.FilteredReadCalls.Should().Be(3);
        list.Count(x => x.Name.StartsWith("sk")).Should().Be(LocalizedTranslationRows.ReadBatchSize + 1);
    }

    [Fact]
    public void A_batched_read_returns_detached_copies()
    {
        Seed(1);
        var stored = _entityStore.Read().Single();

        var read = Bulk().Read().Single();

        read.Should().NotBeSameAs(stored);
        stored.Name.Should().Be("en0000");
        read.Name.Should().Be("sk0000");
    }

    [Fact]
    public async Task The_async_wrapper_costs_one_translation_read()
    {
        for (var i = 0; i < 3; i++)
        {
            var guid = await _asyncEntityStore.CreateAsync(new TestLocalizableModel { Name = $"en{i}", Code = $"C{i}" });
            await _asyncTranslationStore.CreateAsync(Row(guid, $"sk{i}"));
        }
        var calls = _asyncTranslationStore.FilteredReadCalls;

        var names = (await AsyncBulk().ReadAsync(x => x.Code != "", null, null, null)).Select(x => x.Name).OrderBy(n => n);

        names.Should().Equal("sk0", "sk1", "sk2");
        (_asyncTranslationStore.FilteredReadCalls - calls).Should().Be(1);
    }

    [Fact]
    public async Task The_async_wrapper_reads_once_per_batch_and_not_at_all_for_an_empty_list()
    {
        (await AsyncBulk().ReadAsync(x => x.Code == "none", null, null, null)).Should().BeEmpty();
        _asyncTranslationStore.FilteredReadCalls.Should().Be(0);

        for (var i = 0; i < LocalizedTranslationRows.ReadBatchSize + 1; i++)
        {
            await _asyncEntityStore.CreateAsync(new TestLocalizableModel { Name = $"en{i}", Code = $"C{i}" });
        }

        (await AsyncBulk().ReadAsync(x => x.Code != "", null, null, null)).Should().HaveCount(LocalizedTranslationRows.ReadBatchSize + 1);
        _asyncTranslationStore.FilteredReadCalls.Should().Be(2);
    }

    [Fact]
    public void A_mixed_list_is_translated_like_single_reads_would_translate_it()
    {
        var guid = Guid.NewGuid();
        var withoutGuid = new TestLocalizableModel { Name = "noguid" };
        _translationStore.Create(Row(guid, "sk-name"));
        _translationStore.Create(new EntityTranslationModel { EntityGuid = guid, EntityType = nameof(TestLocalizableModel), FieldName = nameof(TestLocalizableModel.Code), Culture = "sk", Value = "undeclared" });
        _translationStore.Create(new EntityTranslationModel { EntityGuid = guid, EntityType = "Other", FieldName = "Name", Culture = "sk", Value = "other-type" });
        _translationStore.Create(new EntityTranslationModel { EntityGuid = guid, EntityType = nameof(TestLocalizableModel), FieldName = "Name", Culture = "de", Value = "other-culture" });
        var stored = new TestLocalizableModel { Guid = guid, Name = "en", Code = "C" };
        var bulk = Bulk();

        var list = new System.Collections.Generic.List<TestLocalizableModel> { stored, withoutGuid, stored };
        var localized = (System.Collections.Generic.List<TestLocalizableModel>)typeof(LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>)
            .GetMethod("LocalizeAll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(bulk, new object[] { list })!;

        _translationStore.FilteredReadCalls.Should().Be(1);
        localized.Select(x => (x.Name, x.Code)).Should().Equal(("sk-name", "C"), ("noguid", ""), ("sk-name", "C"));
        localized[1].Should().BeSameAs(withoutGuid);
        localized[0].Should().NotBeSameAs(stored).And.NotBeSameAs(localized[2]);
        stored.Name.Should().Be("en");
    }
}
