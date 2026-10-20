using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Localization.Translations;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// TASK-682 — <see cref="IEntityTranslations{T}"/>: each method's result, its query count, its refusals, and that it
/// agrees with the store wrappers about keys and fallback.
/// </summary>
public class EntityTranslationsTests
{
    private static readonly CultureInfo Sk = new("sk");
    private static readonly CultureInfo Cs = new("cs");
    private static readonly CultureInfo En = new("en");

    private readonly InMemoryAsyncBulkStore<TestLocalizableModel> _entities = new();
    private readonly InMemoryAsyncBulkStore<EntityTranslationModel> _translations = new();
    private readonly TestEntityLocalizationContext _context = new() { CurrentCulture = En, DefaultCulture = En };

    private EntityTranslations<TestLocalizableModel> Api() => new(_entities, _translations, _context);

    private static EntityTranslationModel Row(Guid guid, string field, string value, string culture = "sk", string type = nameof(TestLocalizableModel)) => new()
    {
        EntityGuid = guid, EntityType = type, FieldName = field, Culture = culture, Value = value,
    };

    private async Task<Guid> Entity(string name, string code = "")
    {
        var guid = Guid.NewGuid();
        await _entities.CreateAsync(new TestLocalizableModel { Guid = guid, Name = name, Code = code });
        return guid;
    }

    private async Task Translate(Guid guid, string field, string value, string culture = "sk")
        => await _translations.CreateAsync(Row(guid, field, value, culture));

    private (int Entities, int Translations) Reads() => (_entities.FilteredReadCalls, _translations.FilteredReadCalls);

    private static (int, int) Delta((int, int) before, (int, int) after) => (after.Item1 - before.Item1, after.Item2 - before.Item2);

    private List<EntityTranslationModel> AllRows() => _translations.ReadAsync(default(CancellationToken)).Result.ToList();

    // ------------------------------------------------------------------ GetAsync

    [Fact]
    public async Task Get_returns_one_language_for_a_list_in_one_read_with_declared_fields_only()
    {
        var a = await Entity("a");
        var b = await Entity("b");
        var untranslated = await Entity("c");
        await Translate(a, "Name", "sk-a");
        await Translate(a, "Description", "popis-a");
        await Translate(a, "Code", "nie je lokalizovatelne");
        await Translate(b, "Name", "sk-b");
        await Translate(b, "Name", "cs-b", "cs");

        var result = await Api().GetAsync(new[] { a, b, untranslated }, Sk);

        _translations.FilteredReadCalls.Should().Be(1);
        result.Keys.Should().BeEquivalentTo(new[] { a, b });
        result[a].Should().BeEquivalentTo(new Dictionary<string, string> { ["Name"] = "sk-a", ["Description"] = "popis-a" });
        result[b].Should().BeEquivalentTo(new Dictionary<string, string> { ["Name"] = "sk-b" });
    }

    [Fact]
    public async Task Get_reads_once_per_batch_and_not_at_all_for_no_ids()
    {
        var ids = Enumerable.Range(0, LocalizedTranslationRows.ReadBatchSize + 1).Select(_ => Guid.NewGuid()).ToList();

        await Api().GetAsync(Array.Empty<Guid>(), Sk);
        _translations.FilteredReadCalls.Should().Be(0);

        await Api().GetAsync(ids, Sk);
        _translations.FilteredReadCalls.Should().Be(2);
    }

    [Fact]
    public async Task An_entity_whose_only_rows_are_for_undeclared_fields_reads_as_untranslated()
    {
        var a = await Entity("a");
        await Translate(a, "Code", "pozostatok");
        await Translate(a, "Code", "pozustatek", "cs");

        (await Api().GetAsync(new[] { a }, Sk)).Should().BeEmpty();
        (await Api().GetAllCulturesAsync(a)).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ GetAllCulturesAsync

    [Fact]
    public async Task Every_culture_of_one_entity_is_one_read()
    {
        var a = await Entity("a");
        await Translate(a, "Name", "sk-a");
        await Translate(a, "Name", "cs-a", "cs");
        await Translate(a, "Description", "popis", "cs");
        await _translations.CreateAsync(Row(a, "Name", "iny typ", type: "Other"));

        var all = await Api().GetAllCulturesAsync(a);

        _translations.FilteredReadCalls.Should().Be(1);
        all.Keys.Should().BeEquivalentTo("sk", "cs");
        all["cs"].Should().BeEquivalentTo(new Dictionary<string, string> { ["Name"] = "cs-a", ["Description"] = "popis" });
        all["sk"].Should().BeEquivalentTo(new Dictionary<string, string> { ["Name"] = "sk-a" });
    }

    // ------------------------------------------------------------------ SetAsync

    [Fact]
    public async Task Set_writes_one_language_in_one_read_and_at_most_three_bulk_calls()
    {
        var a = await Entity("a");
        await Translate(a, "Name", "stare");
        await Translate(a, "Description", "zmaz ma");
        var before = (_translations.FilteredReadCalls, _translations.CreateCalls, _translations.UpdateCalls, _translations.DeleteCalls);

        await Api().SetAsync(a, Sk, new Dictionary<string, string?> { ["Name"] = "nove", ["Description"] = null });

        (_translations.FilteredReadCalls - before.Item1, _translations.CreateCalls - before.Item2,
            _translations.UpdateCalls - before.Item3, _translations.DeleteCalls - before.Item4).Should().Be((1, 0, 1, 1));
        (await Api().GetAsync(new[] { a }, Sk))[a].Should().BeEquivalentTo(new Dictionary<string, string> { ["Name"] = "nove" });
    }

    [Fact]
    public async Task A_value_set_through_the_api_is_what_the_wrapper_reads_in_that_culture()
    {
        var a = await Entity("Chair");
        await Api().SetAsync(a, Sk, new Dictionary<string, string?> { ["Name"] = "Stolicka" });
        _context.CurrentCulture = Sk;
        var wrapper = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(_entities, _translations, _context);

        (await wrapper.ReadAsync(a))!.Name.Should().Be("Stolicka");

        await wrapper.UpdateAsync(new TestLocalizableModel { Guid = a, Name = "Kreslo" });
        (await Api().GetAsync(new[] { a }, Sk))[a]["Name"].Should().Be("Kreslo");
    }

    [Fact]
    public async Task Set_refuses_the_default_culture_an_empty_id_and_an_undeclared_field_and_writes_nothing()
    {
        var a = await Entity("a");
        var api = Api();

        await api.Invoking(x => x.SetAsync(a, En, new Dictionary<string, string?> { ["Name"] = "x" }))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*default culture*");
        await api.Invoking(x => x.SetAsync(Guid.Empty, Sk, new Dictionary<string, string?> { ["Name"] = "x" }))
            .Should().ThrowAsync<ArgumentException>();
        await api.Invoking(x => x.SetAsync(a, Sk, new Dictionary<string, string?> { ["Name"] = "x", ["Code"] = "y" }))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*'Code' is not a localizable field*");

        (_translations.FilteredReadCalls, _translations.CreateCalls).Should().Be((0, 0));
    }

    [Fact]
    public async Task A_culture_without_a_name_the_default_culture_on_removal_and_an_empty_id_are_refused()
    {
        var a = await Entity("a");
        var api = Api();

        await api.Invoking(x => x.SetAsync(a, CultureInfo.InvariantCulture, new Dictionary<string, string?> { ["Name"] = "x" }))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*named culture*");
        await api.Invoking(x => x.GetAsync(new[] { a }, CultureInfo.InvariantCulture)).Should().ThrowAsync<ArgumentException>();
        await api.Invoking(x => x.FindEntityAsync("Name", CultureInfo.InvariantCulture, "x")).Should().ThrowAsync<ArgumentException>();
        await api.Invoking(x => x.RemoveCultureAsync(a, En)).Should().ThrowAsync<ArgumentException>().WithMessage("*default culture*");
        await api.Invoking(x => x.GetAllCulturesAsync(Guid.Empty)).Should().ThrowAsync<ArgumentException>();
        (_translations.FilteredReadCalls, _translations.DeleteCalls).Should().Be((0, 0));
    }

    [Fact]
    public async Task Set_with_no_fields_makes_no_call()
    {
        await Api().SetAsync(await Entity("a"), Sk, new Dictionary<string, string?>());

        (_translations.FilteredReadCalls, _translations.CreateCalls).Should().Be((0, 0));
    }

    // ------------------------------------------------------------------ RemoveCultureAsync / RemoveEntitiesAsync

    [Fact]
    public async Task Removing_a_culture_leaves_the_others_and_other_types_alone()
    {
        var a = await Entity("a");
        await Translate(a, "Name", "sk-a");
        await Translate(a, "Name", "cs-a", "cs");
        await _translations.CreateAsync(Row(a, "Name", "iny typ", type: "Other"));

        await Api().RemoveCultureAsync(a, Sk);

        AllRows().Select(r => r.Value).Should().BeEquivalentTo("cs-a", "iny typ");
    }

    [Fact]
    public async Task Removing_entities_is_one_delete_and_an_empty_list_makes_none()
    {
        var a = await Entity("a");
        var b = await Entity("b");
        var kept = await Entity("c");
        foreach (var guid in new[] { a, b, kept })
        {
            await Translate(guid, "Name", "sk");
            await Translate(guid, "Name", "cs", "cs");
        }
        var api = Api();

        await api.RemoveEntitiesAsync(Array.Empty<Guid>());
        _translations.DeleteCalls.Should().Be(0);

        await api.RemoveEntitiesAsync(new[] { a, b });
        _translations.DeleteCalls.Should().Be(1);
        AllRows().Should().OnlyContain(r => r.EntityGuid == kept).And.HaveCount(2);
    }

    // ------------------------------------------------------------------ FindEntityAsync

    [Fact]
    public async Task Find_resolves_a_translated_value_to_its_entity_in_at_most_four_reads()
    {
        var chair = await Entity("chair");
        await Entity("table");
        await Entity("stolicka-base", code: "other");
        await Translate(chair, "Name", "stolicka");
        var before = Reads();

        (await Api().FindEntityAsync("Name", Sk, "stolicka")).Should().Be(chair);

        Delta(before, Reads()).Should().Be((2, 1),
            "no entity has 'stolicka' as its base value, so there are no candidates to check for translations");

        var untranslated = await Entity("lavica");
        await Translate(chair, "Description", "lavica");
        before = Reads();

        (await Api().FindEntityAsync("Name", Sk, "lavica")).Should().Be(untranslated);

        Delta(before, Reads()).Should().Be((2, 2), "a hash lookup, the base candidates, their translations, the final read");
    }

    [Fact]
    public async Task Find_falls_back_to_the_base_value_of_an_untranslated_entity_only()
    {
        var untranslated = await Entity("lavica");
        var translated = await Entity("stol");
        await Translate(translated, "Name", "stolik");
        var api = Api();

        (await api.FindEntityAsync("Name", Sk, "lavica")).Should().Be(untranslated);
        (await api.FindEntityAsync("Name", Sk, "stol")).Should().BeNull("a translated entity matches only on its translation");
        (await api.FindEntityAsync("Name", Sk, "nic")).Should().BeNull();
    }

    [Fact]
    public async Task Find_on_the_default_culture_tests_the_base_column()
    {
        var chair = await Entity("chair");
        await Translate(chair, "Name", "stolicka");
        var api = Api();

        (await api.FindEntityAsync("Name", En, "chair")).Should().Be(chair);
        (await api.FindEntityAsync("Name", En, "stolicka")).Should().BeNull();
        _translations.FilteredReadCalls.Should().Be(0);
    }

    [Fact]
    public async Task Find_refuses_an_ambiguous_value_naming_both_entities()
    {
        var a = await Entity("a");
        var b = await Entity("b");
        await Translate(a, "Name", "rovnake");
        await Translate(b, "Name", "rovnake");

        var act = () => Api().FindEntityAsync("Name", Sk, "rovnake");

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(a.ToString()).And.Contain(b.ToString());
    }

    [Fact]
    public async Task Find_and_exists_refuse_an_undeclared_field()
    {
        await Api().Invoking(x => x.FindEntityAsync("Code", Sk, "x")).Should().ThrowAsync<ArgumentException>();
        await Api().Invoking(x => x.ExistsAsync("Code", Sk, "x")).Should().ThrowAsync<ArgumentException>();
    }

    // ------------------------------------------------------------------ ExistsAsync

    [Fact]
    public async Task Exists_sees_a_translated_value_except_on_the_entity_that_holds_it()
    {
        var chair = await Entity("chair");
        await Translate(chair, "Name", "stolicka");
        var api = Api();

        (await api.ExistsAsync("Name", Sk, "stolicka")).Should().BeTrue();
        (await api.ExistsAsync("Name", Sk, "stolicka", exceptEntityGuid: chair)).Should().BeFalse();
        (await api.ExistsAsync("Name", Sk, "stolicka", exceptEntityGuid: Guid.NewGuid())).Should().BeTrue();
    }

    [Fact]
    public async Task Exists_counts_the_base_value_an_untranslated_entity_answers_with()
    {
        var untranslated = await Entity("lavica");
        var translated = await Entity("stol");
        await Translate(translated, "Name", "stolik");
        var api = Api();

        (await api.ExistsAsync("Name", Sk, "lavica")).Should().BeTrue("a request in sk resolves 'lavica' to the untranslated entity");
        (await api.ExistsAsync("Name", Sk, "lavica", exceptEntityGuid: untranslated)).Should().BeFalse();
        (await api.ExistsAsync("Name", Sk, "stol")).Should().BeFalse("the translated entity answers 'stolik' in sk, not its base value");
    }

    // ------------------------------------------------------------------ LocalizeAsync

    [Fact]
    public async Task Localize_applies_a_given_language_in_one_read_as_detached_copies_in_order()
    {
        var a = await Entity("a-en");
        var b = await Entity("b-en");
        await Translate(a, "Name", "a-sk");
        var loaded = new[] { (await _entities.ReadAsync(b))!, (await _entities.ReadAsync(a))! };

        var localized = await Api().LocalizeAsync(loaded, Sk);

        _translations.FilteredReadCalls.Should().Be(1);
        localized.Select(x => x.Name).Should().Equal("b-en", "a-sk");
        localized.Should().NotContain(x => ReferenceEquals(x, loaded[0]) || ReferenceEquals(x, loaded[1]));
        loaded[1].Name.Should().Be("a-en", "the loaded entity is not modified");
    }

    [Fact]
    public async Task Localize_on_the_default_culture_returns_the_entities_unchanged_without_a_read()
    {
        var loaded = new[] { (await _entities.ReadAsync(await Entity("a")))! };

        var localized = await Api().LocalizeAsync(loaded, En);

        localized.Should().ContainSingle().Which.Should().BeSameAs(loaded[0]);
        _translations.FilteredReadCalls.Should().Be(0);
    }

    // ------------------------------------------------------------------ construction and DI

    [Fact]
    public void A_localized_wrapper_as_the_entity_store_is_refused_directly_or_deeper_in_the_chain()
    {
        var localized = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(_entities, _translations, _context);

        var direct = () => new EntityTranslations<TestLocalizableModel>(localized, _translations, _context);
        var deeper = () => new EntityTranslations<TestLocalizableModel>(new ChainedStore(localized), _translations, _context);

        direct.Should().Throw<ArgumentException>().WithMessage("*localized store wrapper*");
        deeper.Should().Throw<ArgumentException>().WithMessage("*localized store wrapper*");
        new EntityTranslations<TestLocalizableModel>(new ChainedStore(_entities), _translations, _context).Should().NotBeNull();
    }

    [Fact]
    public async Task The_di_extension_registers_the_api_over_the_containers_stores()
    {
        var a = await Entity("a");
        await Translate(a, "Name", "sk-a");
        var services = new ServiceCollection()
            .AddSingleton<IAsyncBulkStore<TestLocalizableModel>>(_entities)
            .AddSingleton<IAsyncBulkStore<EntityTranslationModel>>(_translations)
            .AddSingleton<IEntityLocalizationContext>(_context)
            .AddEntityTranslations<TestLocalizableModel>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var api = scope.ServiceProvider.GetRequiredService<IEntityTranslations<TestLocalizableModel>>();

        (await api.FindEntityAsync("Name", Sk, "sk-a")).Should().Be(a);
    }

    [Fact]
    public void The_di_factory_overload_takes_the_store_the_localized_wrapper_wraps()
    {
        var localized = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(_entities, _translations, _context);
        var services = new ServiceCollection()
            .AddSingleton<IAsyncBulkStore<TestLocalizableModel>>(localized)
            .AddSingleton<IAsyncBulkStore<EntityTranslationModel>>(_translations)
            .AddSingleton<IEntityLocalizationContext>(_context);
        using var refused = new ServiceCollection().Add(services).AddEntityTranslations<TestLocalizableModel>().BuildServiceProvider();
        using var wired = new ServiceCollection().Add(services)
            .AddEntityTranslations<TestLocalizableModel, EntityTranslationModel>(_ => _entities, sp => sp.GetRequiredService<IAsyncBulkStore<EntityTranslationModel>>())
            .BuildServiceProvider();

        var act = () => refused.CreateScope().ServiceProvider.GetRequiredService<IEntityTranslations<TestLocalizableModel>>();

        act.Should().Throw<ArgumentException>();
        wired.CreateScope().ServiceProvider.GetRequiredService<IEntityTranslations<TestLocalizableModel>>().Should().NotBeNull();
    }

    /// <summary>Another decorator (tenant, soft delete…) sitting over the store it is given.</summary>
    private sealed class ChainedStore : InMemoryAsyncBulkStore<TestLocalizableModel>, IStoreWrapper
    {
        private readonly object _inner;

        public ChainedStore(object inner) => _inner = inner;

        public object? GetInnerStore() => _inner;
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static IServiceCollection Add(this IServiceCollection target, IServiceCollection source)
    {
        foreach (var descriptor in source)
        {
            target.Add(descriptor);
        }
        return target;
    }
}
