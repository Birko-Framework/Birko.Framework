using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// TASK-690 — translation writes are batched: saving one entity-language costs one read of its rows and at most one bulk
/// delete, update and create; the wrappers' collection, filter and delete paths go through the same producer. Each test
/// counts the translation store's calls and checks the rows.
/// </summary>
public class LocalizationBatchedWriteTests
{
    private const string Type = nameof(TestLocalizableModel);

    private readonly InMemoryBulkStore<TestLocalizableModel> _entityStore = new();
    private readonly InMemoryBulkStore<EntityTranslationModel> _translations = new();
    private readonly InMemoryAsyncBulkStore<TestLocalizableModel> _asyncEntityStore = new();
    private readonly InMemoryAsyncBulkStore<EntityTranslationModel> _asyncTranslations = new();
    private readonly TestEntityLocalizationContext _context = new()
    {
        CurrentCulture = new CultureInfo("sk"),
        DefaultCulture = new CultureInfo("en"),
    };

    private LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel> Bulk()
        => new(_entityStore, _translations, _context);

    private AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel> AsyncBulk()
        => new(_asyncEntityStore, _asyncTranslations, _context);

    private static EntityTranslationModel Row(Guid guid, string field, string value, string culture = "sk", string type = Type) => new()
    {
        EntityGuid = guid, EntityType = type, FieldName = field, Culture = culture, Value = value,
    };

    private static Dictionary<string, string?> Values(params (string Field, string? Value)[] values)
        => values.ToDictionary(v => v.Field, v => v.Value);

    private static (int Reads, int Creates, int Updates, int Deletes) Calls(InMemoryBulkStore<EntityTranslationModel> store)
        => (store.FilteredReadCalls, store.CreateCalls, store.UpdateCalls, store.DeleteCalls);

    private static (int Reads, int Creates, int Updates, int Deletes) Calls(InMemoryAsyncBulkStore<EntityTranslationModel> store)
        => (store.FilteredReadCalls, store.CreateCalls, store.UpdateCalls, store.DeleteCalls);

    private List<(string Field, string Culture, string Value)> RowsOf(Guid guid)
        => _translations.Read().Where(t => t.EntityGuid == guid)
            .Select(t => (t.FieldName, t.Culture, t.Value)).OrderBy(t => t.FieldName).ThenBy(t => t.Culture).ToList();

    private async Task<List<(string Field, string Culture, string Value)>> AsyncRowsOf(Guid guid)
        => (await _asyncTranslations.ReadAsync(default(System.Threading.CancellationToken)))
            .Where(t => t.EntityGuid == guid)
            .Select(t => (t.FieldName, t.Culture, t.Value)).OrderBy(t => t.FieldName).ThenBy(t => t.Culture).ToList();

    // ------------------------------------------------------------------ the row producer

    [Fact]
    public void Saving_a_new_a_changed_and_a_removed_field_costs_one_read_and_one_call_of_each_kind()
    {
        var guid = Guid.NewGuid();
        _translations.Create(Row(guid, "Name", "stare"));
        _translations.Create(Row(guid, "Description", "zmaz ma"));
        var before = Calls(_translations);

        LocalizedTranslationRows.Save(_translations, guid, Type, "sk",
            new Dictionary<string, string?> { ["Name"] = "nove", ["Description"] = null, ["Code"] = "pridane" });

        Delta(before, Calls(_translations)).Should().Be((1, 1, 1, 1));
        RowsOf(guid).Should().Equal(("Code", "sk", "pridane"), ("Name", "sk", "nove"));
    }

    [Fact]
    public async Task The_async_save_costs_one_read_and_one_call_of_each_kind_too()
    {
        var guid = Guid.NewGuid();
        await _asyncTranslations.CreateAsync(Row(guid, "Name", "stare"));
        await _asyncTranslations.CreateAsync(Row(guid, "Description", "zmaz ma"));
        var before = Calls(_asyncTranslations);

        await LocalizedTranslationRows.SaveAsync(_asyncTranslations, guid, Type, "sk",
            new Dictionary<string, string?> { ["Name"] = "nove", ["Description"] = null, ["Code"] = "pridane" }, default);

        Delta(before, Calls(_asyncTranslations)).Should().Be((1, 1, 1, 1));
        (await AsyncRowsOf(guid)).Should().Equal(("Code", "sk", "pridane"), ("Name", "sk", "nove"));
    }

    [Fact]
    public void A_kind_of_write_with_nothing_to_do_is_not_called()
    {
        var guid = Guid.NewGuid();

        LocalizedTranslationRows.Save(_translations, guid, Type, "sk", Values(("Name", "nove"), ("Description", "popis")));

        Calls(_translations).Should().Be((1, 1, 0, 0));
    }

    [Fact]
    public void Saving_no_values_makes_no_call_at_all()
    {
        LocalizedTranslationRows.Save(_translations, Guid.NewGuid(), Type, "sk", new Dictionary<string, string?>());
        LocalizedTranslationRows.SaveMany(_translations, Type, "sk",
            Array.Empty<(Guid, IReadOnlyDictionary<string, string?>)>());

        Calls(_translations).Should().Be((0, 0, 0, 0));
    }

    [Fact]
    public void Many_entities_are_saved_in_one_read_and_one_create()
    {
        var guids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();

        LocalizedTranslationRows.SaveMany(_translations, Type, "sk",
            guids.Select(g => (g, (IReadOnlyDictionary<string, string?>)Values(("Name", $"n{g}"), ("Description", $"d{g}")))).ToList());

        Calls(_translations).Should().Be((1, 1, 0, 0));
        _translations.Read().Should().HaveCount(6);
    }

    [Fact]
    public void Reading_the_existing_rows_is_batched_like_any_list_read()
    {
        var guids = Enumerable.Range(0, LocalizedTranslationRows.ReadBatchSize + 1).Select(_ => Guid.NewGuid()).ToList();

        LocalizedTranslationRows.SaveMany(_translations, Type, "sk",
            guids.Select(g => (g, (IReadOnlyDictionary<string, string?>)Values(("Name", "n")))).ToList());

        Calls(_translations).Should().Be((2, 1, 0, 0));
        _translations.Read().Should().HaveCount(guids.Count);
    }

    [Fact]
    public void An_entity_listed_twice_is_merged_and_its_later_value_wins()
    {
        var guid = Guid.NewGuid();

        LocalizedTranslationRows.SaveMany(_translations, Type, "sk", new[]
        {
            (guid, (IReadOnlyDictionary<string, string?>)Values(("Name", "prve"), ("Description", "popis"))),
            (guid, (IReadOnlyDictionary<string, string?>)Values(("Name", "druhe"))),
        });

        RowsOf(guid).Should().Equal(("Description", "sk", "popis"), ("Name", "sk", "druhe"));
    }

    [Fact]
    public void Rows_of_another_culture_or_another_type_are_neither_read_into_the_plan_nor_touched()
    {
        var guid = Guid.NewGuid();
        _translations.Create(Row(guid, "Name", "cesky", culture: "cs"));
        _translations.Create(Row(guid, "Name", "iny typ", type: "Other"));

        LocalizedTranslationRows.Save(_translations, guid, Type, "sk", Values(("Name", null)));
        LocalizedTranslationRows.Save(_translations, guid, Type, "sk", Values(("Name", "slovensky")));

        _translations.Read().Where(t => t.EntityGuid == guid).Select(t => (t.EntityType, t.Culture, t.Value))
            .Should().BeEquivalentTo(new[] { (Type, "cs", "cesky"), ("Other", "sk", "iny typ"), (Type, "sk", "slovensky") });
    }

    [Fact]
    public void A_written_row_carries_its_value_hash()
    {
        var guid = Guid.NewGuid();
        _translations.Create(Row(guid, "Description", "stare"));

        LocalizedTranslationRows.Save(_translations, guid, Type, "sk", Values(("Name", "nove"), ("Description", "zmenene")));

        _translations.Read().Where(t => t.EntityGuid == guid)
            .Should().OnlyContain(t => t.ValueHash == LocalizedValueHash.Of(t.Value));
    }

    // ------------------------------------------------------------------ the unique-key retry, batched (SH-M074)

    [Fact]
    public void A_batch_refused_because_another_writer_stored_one_key_updates_that_row_and_creates_the_rest()
    {
        var guid = Guid.NewGuid();
        var raced = false;
        _translations.BeforeCreate = row =>
        {
            if (raced || row.FieldName != "Name")
            {
                return;
            }
            raced = true;
            _translations.BeforeCreate = null;
            _translations.Create(Row(row.EntityGuid, "Name", "druhy zapisovatel"));
            throw StoreException.DuplicateCreate(typeof(EntityTranslationModel));
        };

        LocalizedTranslationRows.Save(_translations, guid, Type, "sk", Values(("Name", "nase"), ("Description", "popis")));

        raced.Should().BeTrue("the test must actually have raced");
        RowsOf(guid).Should().Equal(("Description", "sk", "popis"), ("Name", "sk", "nase"));
    }

    [Fact]
    public async Task The_async_batch_retries_the_same_way()
    {
        var guid = Guid.NewGuid();
        var raced = false;
        _asyncTranslations.BeforeCreate = row =>
        {
            if (raced || row.FieldName != "Name")
            {
                return;
            }
            raced = true;
            _asyncTranslations.BeforeCreate = null;
            _asyncTranslations.CreateAsync(Row(row.EntityGuid, "Name", "druhy zapisovatel")).GetAwaiter().GetResult();
            throw StoreException.DuplicateCreate(typeof(EntityTranslationModel));
        };

        await LocalizedTranslationRows.SaveAsync(_asyncTranslations, guid, Type, "sk",
            Values(("Name", "nase"), ("Description", "popis")), default);

        raced.Should().BeTrue();
        (await AsyncRowsOf(guid)).Should().Equal(("Description", "sk", "popis"), ("Name", "sk", "nase"));
    }

    [Fact]
    public void A_batch_refused_again_after_the_retry_rethrows_the_original_refusal()
    {
        var guid = Guid.NewGuid();
        StoreException? first = null;
        _translations.BeforeCreate = row =>
        {
            if (first == null)
            {
                first = StoreException.DuplicateCreate(typeof(EntityTranslationModel));
                var held = Row(row.EntityGuid, "Name", "druhy zapisovatel");
                _translations.BeforeCreate = _ => throw StoreException.DuplicateCreate(typeof(EntityTranslationModel));
                StoreDirect(held);
                throw first;
            }
        };

        var act = () => LocalizedTranslationRows.Save(_translations, guid, Type, "sk", Values(("Name", "nase"), ("Description", "popis")));

        act.Should().Throw<StoreUniqueViolationException>().Which.Should().BeSameAs(first);
    }

    [Fact]
    public void A_create_refusal_that_cannot_be_retried_leaves_the_updates_and_deletes_unapplied()
    {
        var guid = Guid.NewGuid();
        _translations.Create(Row(guid, "Name", "stare"));
        _translations.Create(Row(guid, "Description", "zostava"));
        _translations.BeforeCreate = _ => throw StoreException.DuplicateCreate(typeof(EntityTranslationModel));

        var act = () => LocalizedTranslationRows.Save(_translations, guid, Type, "sk",
            Values(("Name", "nove"), ("Description", null), ("Code", "pridane")));

        act.Should().Throw<StoreUniqueViolationException>();
        RowsOf(guid).Should().Equal(("Description", "sk", "zostava"), ("Name", "sk", "stare"));
    }

    /// <summary>Stores a row with no hook running, as another writer's connection would.</summary>
    private void StoreDirect(EntityTranslationModel row)
    {
        var hook = _translations.BeforeCreate;
        _translations.BeforeCreate = null;
        _translations.Create(row);
        _translations.BeforeCreate = hook;
    }

    [Fact]
    public void A_batch_refusal_with_no_key_held_by_anyone_stands()
    {
        _translations.BeforeCreate = _ => throw StoreException.DuplicateCreate(typeof(EntityTranslationModel));

        var act = () => LocalizedTranslationRows.Save(_translations, Guid.NewGuid(), Type, "sk", Values(("Name", "nase")));

        act.Should().Throw<StoreUniqueViolationException>();
        _translations.Read().Should().BeEmpty();
    }

    // ------------------------------------------------------------------ deletes

    [Fact]
    public void Deleting_many_entities_is_one_filter_delete_and_leaves_other_entities_and_types_alone()
    {
        var gone = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var kept = Guid.NewGuid();
        foreach (var guid in gone)
        {
            _translations.Create(Row(guid, "Name", "a"));
            _translations.Create(Row(guid, "Name", "b", culture: "cs"));
        }
        _translations.Create(Row(kept, "Name", "zostava"));
        _translations.Create(Row(gone[0], "Name", "iny typ", type: "Other"));
        var before = Calls(_translations);

        LocalizedTranslationRows.DeleteMany(_translations, gone, Type);

        Delta(before, Calls(_translations)).Should().Be((0, 0, 0, 1));
        _translations.Read().Select(t => t.Value).Should().BeEquivalentTo("zostava", "iny typ");
    }

    [Fact]
    public void Deleting_no_entities_makes_no_call()
    {
        LocalizedTranslationRows.DeleteMany(_translations, Array.Empty<Guid>(), Type);

        Calls(_translations).Should().Be((0, 0, 0, 0));
    }

    [Fact]
    public async Task Deleting_more_entities_than_one_batch_takes_one_delete_per_batch()
    {
        var guids = Enumerable.Range(0, LocalizedTranslationRows.ReadBatchSize + 1).Select(_ => Guid.NewGuid()).ToList();
        await _asyncTranslations.CreateAsync(guids.Select(g => Row(g, "Name", "x")).ToList());
        var before = Calls(_asyncTranslations);

        await LocalizedTranslationRows.DeleteManyAsync(_asyncTranslations, guids, Type, default);

        Delta(before, Calls(_asyncTranslations)).Should().Be((0, 0, 0, 2));
        (await _asyncTranslations.ReadAsync(default(System.Threading.CancellationToken))).Should().BeEmpty();
    }

    [Fact]
    public void Deleting_without_an_entity_type_is_refused()
    {
        var act = () => LocalizedTranslationRows.DeleteMany(_translations, new[] { Guid.NewGuid() }, "");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deleting_one_culture_leaves_the_others_and_other_types_alone()
    {
        var guid = Guid.NewGuid();
        _translations.Create(Row(guid, "Name", "sk1"));
        _translations.Create(Row(guid, "Description", "sk2"));
        _translations.Create(Row(guid, "Name", "cs1", culture: "cs"));
        _translations.Create(Row(guid, "Name", "iny typ", type: "Other"));

        LocalizedTranslationRows.DeleteCulture(_translations, guid, Type, "sk");

        _translations.Read().Select(t => t.Value).Should().BeEquivalentTo("cs1", "iny typ");
    }

    [Fact]
    public async Task The_async_culture_delete_leaves_the_others_alone_too()
    {
        var guid = Guid.NewGuid();
        await _asyncTranslations.CreateAsync(Row(guid, "Name", "sk1"));
        await _asyncTranslations.CreateAsync(Row(guid, "Name", "cs1", culture: "cs"));

        await LocalizedTranslationRows.DeleteCultureAsync(_asyncTranslations, guid, Type, "sk", default);

        (await AsyncRowsOf(guid)).Should().Equal(("Name", "cs", "cs1"));
    }

    [Fact]
    public void Deleting_a_culture_without_naming_it_is_refused()
    {
        var act = () => LocalizedTranslationRows.DeleteCulture(_translations, Guid.NewGuid(), Type, "");

        act.Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------------ every culture of one entity

    [Fact]
    public async Task Every_culture_of_one_entity_is_one_read_grouped_by_culture()
    {
        var guid = Guid.NewGuid();
        _translations.Create(Row(guid, "Name", "sk1"));
        _translations.Create(Row(guid, "Description", "sk2"));
        _translations.Create(Row(guid, "Name", "cs1", culture: "cs"));
        _translations.Create(Row(guid, "Name", "iny typ", type: "Other"));
        _translations.Create(Row(Guid.NewGuid(), "Name", "ina entita"));

        var all = LocalizedTranslationRows.ReadAllCultures(_translations, guid, Type);

        _translations.FilteredReadCalls.Should().Be(1);
        all.Keys.Should().BeEquivalentTo("sk", "cs");
        all["sk"].Should().BeEquivalentTo(new Dictionary<string, string?> { ["Name"] = "sk1", ["Description"] = "sk2" });
        all["cs"].Should().BeEquivalentTo(new Dictionary<string, string?> { ["Name"] = "cs1" });

        await _asyncTranslations.CreateAsync(Row(guid, "Name", "sk1"));
        (await LocalizedTranslationRows.ReadAllCulturesAsync(_asyncTranslations, guid, Type, default))["sk"]["Name"]
            .Should().Be("sk1");
    }

    // ------------------------------------------------------------------ the wrappers

    [Fact]
    public void A_collection_create_saves_every_entitys_translations_in_one_read_and_one_create()
    {
        var items = Enumerable.Range(0, 3).Select(i => new TestLocalizableModel { Guid = Guid.NewGuid(), Name = $"n{i}", Description = $"d{i}" }).ToList();

        Bulk().Create(items);

        Calls(_translations).Should().Be((1, 1, 0, 0));
        _translations.Read().Should().HaveCount(6);
    }

    [Fact]
    public async Task An_async_collection_create_saves_every_entitys_translations_in_one_read_and_one_create()
    {
        var items = Enumerable.Range(0, 3).Select(i => new TestLocalizableModel { Guid = Guid.NewGuid(), Name = $"n{i}", Description = $"d{i}" }).ToList();

        await AsyncBulk().CreateAsync(items);

        Calls(_asyncTranslations).Should().Be((1, 1, 0, 0));
        (await _asyncTranslations.ReadAsync(default(System.Threading.CancellationToken))).Should().HaveCount(6);
    }

    [Fact]
    public async Task An_async_collection_update_saves_every_entitys_translations_in_one_read_and_one_update()
    {
        var items = Enumerable.Range(0, 3).Select(i => new TestLocalizableModel { Guid = Guid.NewGuid(), Name = $"n{i}", Description = $"d{i}" }).ToList();
        await AsyncBulk().CreateAsync(items);
        foreach (var item in items)
        {
            item.Name += "!";
        }
        var before = Calls(_asyncTranslations);

        await AsyncBulk().UpdateAsync(items);

        Delta(before, Calls(_asyncTranslations)).Should().Be((1, 0, 1, 0));
        (await _asyncTranslations.ReadAsync(default(System.Threading.CancellationToken))).Where(t => t.FieldName == "Name").Select(t => t.Value)
            .Should().BeEquivalentTo("n0!", "n1!", "n2!");
    }

    [Fact]
    public void Deleting_a_collection_or_by_filter_deletes_its_translations_in_one_call()
    {
        var bulk = Bulk();
        var items = Enumerable.Range(0, 4).Select(i => new TestLocalizableModel { Guid = Guid.NewGuid(), Name = $"n{i}", Code = i < 2 ? "A" : "B" }).ToList();
        bulk.Create(items);
        var before = Calls(_translations);

        bulk.Delete(items.Take(2));
        Delta(before, Calls(_translations)).Should().Be((0, 0, 0, 1));

        before = Calls(_translations);
        bulk.Delete(x => x.Code == "B");
        Delta(before, Calls(_translations)).Should().Be((0, 0, 0, 1));

        _translations.Read().Should().BeEmpty();
    }

    [Fact]
    public async Task An_async_filter_delete_deletes_its_translations_in_one_call()
    {
        var bulk = AsyncBulk();
        await bulk.CreateAsync(Enumerable.Range(0, 3).Select(i => new TestLocalizableModel { Guid = Guid.NewGuid(), Name = $"n{i}", Code = "A" }).ToList());
        var before = Calls(_asyncTranslations);

        await bulk.DeleteAsync(x => x.Code == "A");

        Delta(before, Calls(_asyncTranslations)).Should().Be((0, 0, 0, 1));
        (await _asyncTranslations.ReadAsync(default(System.Threading.CancellationToken))).Should().BeEmpty();
    }

    [Fact]
    public void A_filter_update_reads_the_matched_translations_once_and_saves_them_once()
    {
        var bulk = Bulk();
        bulk.Create(Enumerable.Range(0, 3).Select(i => new TestLocalizableModel { Guid = Guid.NewGuid(), Name = $"sk{i}", Code = "A" }).ToList());
        var before = Calls(_translations);

        bulk.Update(x => x.Code == "A", x => x.Name += "!");

        Delta(before, Calls(_translations)).Should().Be((2, 0, 1, 0), "one read to apply the translations, one to plan the save");
        _translations.Read().Where(t => t.FieldName == "Name").Select(t => t.Value).Should().BeEquivalentTo("sk0!", "sk1!", "sk2!");
    }

    [Fact]
    public async Task An_async_filter_update_reads_the_matched_translations_once_and_saves_them_once()
    {
        var bulk = AsyncBulk();
        await bulk.CreateAsync(Enumerable.Range(0, 3).Select(i => new TestLocalizableModel { Guid = Guid.NewGuid(), Name = $"sk{i}", Code = "A" }).ToList());
        var before = Calls(_asyncTranslations);

        await bulk.UpdateAsync(x => x.Code == "A", x => x.Name += "!");

        Delta(before, Calls(_asyncTranslations)).Should().Be((2, 0, 1, 0));
        (await _asyncTranslations.ReadAsync(default(System.Threading.CancellationToken))).Where(t => t.FieldName == "Name").Select(t => t.Value)
            .Should().BeEquivalentTo("sk0!", "sk1!", "sk2!");
    }

    [Fact]
    public async Task An_async_filter_update_that_fails_part_way_still_saves_the_translations_of_the_entities_it_wrote()
    {
        var bulk = AsyncBulk();
        var first = new TestLocalizableModel { Guid = Guid.NewGuid(), Name = "sk-a", Code = "A" };
        var second = new TestLocalizableModel { Guid = Guid.NewGuid(), Name = "sk-b", Code = "A" };
        await bulk.CreateAsync(new[] { first, second });
        var updates = 0;
        _asyncEntityStore.BeforeUpdate = _ =>
        {
            if (++updates == 2)
            {
                throw new InvalidOperationException("inner write failed");
            }
        };

        var act = async () => await bulk.UpdateAsync(x => x.Code == "A", x => x.Name += "!");

        await act.Should().ThrowAsync<InvalidOperationException>();
        // Which entity the store enumerates first is not specified: exactly one was written, and only its translation moved.
        var values = (await _asyncTranslations.ReadAsync(default(System.Threading.CancellationToken)))
            .Where(t => t.FieldName == "Name").Select(t => t.Value).ToList();
        values.Should().HaveCount(2);
        values.Count(v => v.EndsWith('!')).Should().Be(1);
    }

    [Fact]
    public void A_sync_filter_update_that_fails_part_way_still_saves_the_translations_of_the_entities_it_wrote()
    {
        var bulk = Bulk();
        bulk.Create(new[]
        {
            new TestLocalizableModel { Guid = Guid.NewGuid(), Name = "sk-a", Code = "A" },
            new TestLocalizableModel { Guid = Guid.NewGuid(), Name = "sk-b", Code = "A" },
        });
        var updates = 0;
        _entityStore.BeforeUpdate = _ =>
        {
            if (++updates == 2)
            {
                throw new InvalidOperationException("inner write failed");
            }
        };

        var act = () => bulk.Update(x => x.Code == "A", x => x.Name += "!");

        act.Should().Throw<InvalidOperationException>().WithMessage("inner write failed");
        var values = _translations.Read().Where(t => t.FieldName == "Name").Select(t => t.Value).ToList();
        values.Should().HaveCount(2);
        values.Count(v => v.EndsWith('!')).Should().Be(1);
    }

    [Fact]
    public async Task When_the_translations_cannot_be_saved_after_a_failure_the_original_error_still_wins()
    {
        // Created on the default culture, so the Slovak update creates the translation rows, and that create fails too.
        _context.CurrentCulture = new CultureInfo("en");
        var bulk = AsyncBulk();
        await bulk.CreateAsync(new[]
        {
            new TestLocalizableModel { Guid = Guid.NewGuid(), Name = "a", Code = "A" },
            new TestLocalizableModel { Guid = Guid.NewGuid(), Name = "b", Code = "A" },
        });
        _context.CurrentCulture = new CultureInfo("sk");
        _asyncTranslations.BeforeCreate = _ => throw new InvalidOperationException("translation save failed");
        var updates = 0;
        _asyncEntityStore.BeforeUpdate = _ =>
        {
            if (++updates == 2)
            {
                throw new InvalidOperationException("inner write failed");
            }
        };

        var act = async () => await bulk.UpdateAsync(x => x.Code == "A", x => x.Name += "!");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("inner write failed");
    }

    [Fact]
    public void On_the_default_culture_a_collection_write_captures_and_saves_nothing()
    {
        _context.CurrentCulture = new CultureInfo("en");

        Bulk().Create(Enumerable.Range(0, 3).Select(i => new TestLocalizableModel { Guid = Guid.NewGuid(), Name = $"n{i}" }).ToList());

        Calls(_translations).Should().Be((0, 0, 0, 0));
    }

    private static (int, int, int, int) Delta((int, int, int, int) before, (int, int, int, int) after)
        => (after.Item1 - before.Item1, after.Item2 - before.Item2, after.Item3 - before.Item3, after.Item4 - before.Item4);
}
