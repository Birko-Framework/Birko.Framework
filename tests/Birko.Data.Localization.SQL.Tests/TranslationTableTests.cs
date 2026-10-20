using System.Globalization;
using System.Linq.Expressions;
using Birko.Configuration;
using Birko.Data.Exceptions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.SQL.Mappings;
using Birko.Data.Localization.Translations;
using Birko.Data.Models;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Localization.SQL.Tests;

// Top-level, not nested in the generic test class: a nested type is a different CLR type per closed generic, and the
// mapping registry is keyed by type.
public abstract class TenantBase : AbstractModel
{
    public Guid TenantGuid { get; set; }
}

public sealed class TenantTranslation : TenantBase, IEntityTranslation
{
    public Guid EntityGuid { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string Culture { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string ValueHash { get; set; } = string.Empty;
}

public sealed class Product : AbstractModel, ILocalizable
{
    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
}

public static class TestModels
{
    public const string TenantTable = "BirkoTestTenantTranslations";
    public const string ProductTable = "BirkoTestLocalizedProducts";
}

/// <summary>Products stored in SQL too, so the rewritten filter (OR, NOT IN) runs through the provider's translator.</summary>
public sealed class SqlProduct : AbstractModel, ILocalizable
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
}

public sealed class SqlProductMapping : IModelMapping<SqlProduct>
{
    public void Configure(ModelMap<SqlProduct> map) => map.ToTable(TestModels.ProductTable).HasPrimary(x => x.Guid);
}

/// <summary>
/// TASK-680 on a real database: the unique key the mapping declares exists after schema-ensure (read back from the
/// catalogue, rule 63), refuses a second create of one key, and turns a racing writer into one row (SH-M074); a
/// consumer's own model maps with the same key; TASK-679's batched read renders past one batch.
/// </summary>
public abstract class TranslationTableTests<DB> : IDisposable where DB : AbstractConnector
{
    private readonly ITestOutputHelper _output;

    protected TranslationTableTests(ITestOutputHelper output)
    {
        _output = output;
        TranslationMappings.EnsureApplied();
    }

    protected abstract string Label { get; }

    /// <summary>The environment variable naming the server, or null for an embedded provider that always runs.</summary>
    protected abstract string? HostVariable { get; }

    protected abstract PasswordSettings Settings();

    protected abstract IIndexManager Indexes(AbstractConnectorBase connector);

    /// <summary>
    /// False where the provider cannot hold the key as mapped: MySQL refuses a key over 3 072 bytes, and four columns
    /// of VARCHAR(255) utf8mb4 make 3 204 (measured, ERROR 1071). The tests that need the key skip there, and
    /// <see cref="A_provider_that_cannot_hold_the_key_reports_it"/> pins the reported failure until the column
    /// lengths can be bounded (TASK-684).
    /// </summary>
    protected virtual bool HoldsTheKey => true;

    /// <summary>
    /// What a racing writer does to an upsert running inside the caller's <c>SqlUnitOfWork</c>: true — the retry
    /// succeeds in the transaction (SQL Server, measured); false — the refused INSERT aborts the transaction and the
    /// upsert surfaces the unique violation (PostgreSQL, measured: 25P02 otherwise hid it); null — not exercised here.
    /// </summary>
    protected virtual bool? RetriesInsideATransaction => null;

    private bool RequireKey()
    {
        if (HoldsTheKey) return true;
        _output.WriteLine($"SKIPPED on {Label}: the provider cannot hold the unique key as mapped (TASK-684).");
        return false;
    }

    public virtual void Dispose()
    {
    }

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    protected bool RequireServer()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable)))
        {
            return true;
        }
        var message = $"SKIPPED: no live {Label}. Set {HostVariable} to exercise this test; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return false;
    }

    private AsyncDataBaseBulkStore<DB, TTranslation> Store<TTranslation>() where TTranslation : AbstractModel, new()
    {
        var store = new AsyncDataBaseBulkStore<DB, TTranslation>();
        store.SetSettings(Settings());
        return store;
    }

    private static EntityTranslationModel Row(Guid entity, string value, string field = "Name") => new()
    {
        EntityGuid = entity, EntityType = "Product", FieldName = field, Culture = "sk", Value = value,
    };

    private async Task<List<string>> KeyColumnsAsync<TTranslation>(AsyncDataBaseBulkStore<DB, TTranslation> store, string table)
        where TTranslation : AbstractModel, new()
    {
        var info = await Indexes(store.Connector!).GetInfoAsync(EntityTranslationMapping<EntityTranslationModel>.KeyIndexName(table), table);
        info.Should().NotBeNull($"schema-ensure must create {EntityTranslationMapping<EntityTranslationModel>.KeyIndexName(table)} on {Label}");
        info!.Unique.Should().BeTrue();
        // PostgreSQL folds the unquoted column names to lower case (rule 17); the order is what is asserted.
        return info.Fields.Select(f => f.Name.ToLowerInvariant()).ToList();
    }

    [Fact]
    public async Task Schema_ensure_creates_the_unique_key_over_the_four_columns()
    {
        if (!RequireServer() || !RequireKey()) return;
        var store = Store<EntityTranslationModel>();
        await store.InitAsync();

        (await KeyColumnsAsync(store, EntityTranslationMapping<EntityTranslationModel>.DefaultTable))
            .Should().Equal("entityguid", "entitytype", "fieldname", "culture");
    }

    [Fact]
    public async Task A_second_create_of_one_key_is_refused_by_the_store()
    {
        if (!RequireServer() || !RequireKey()) return;
        var store = Store<EntityTranslationModel>();
        var entity = Guid.NewGuid();
        await store.CreateAsync(Row(entity, "first"));

        var act = async () => await store.CreateAsync(Row(entity, "second"));

        await act.Should().ThrowAsync<StoreUniqueViolationException>();
        (await store.CountAsync(t => t.EntityGuid == entity)).Should().Be(1);
    }

    [Fact]
    public async Task A_racing_writer_leaves_one_row_carrying_the_last_value()
    {
        if (!RequireServer() || !RequireKey()) return;
        var inner = Store<EntityTranslationModel>();
        var entity = Guid.NewGuid();
        // The other writer stores the key after our read found nothing, and before our create.
        var racing = new RacingStore<EntityTranslationModel>(inner, () => inner.CreateAsync(Row(entity, "other writer")));

        await LocalizedTranslationRows.SaveAsync(racing, entity, "Product", "sk",
            new Dictionary<string, string?> { ["Name"] = "ours" }, CancellationToken.None);

        var rows = (await inner.ReadAsync(t => t.EntityGuid == entity, null, null, null)).ToList();
        rows.Select(r => r.Value).Should().Equal("ours");
        racing.Raced.Should().BeTrue("the test must actually have raced");
    }

    [Fact]
    public async Task A_racing_writer_inside_the_callers_transaction_is_retried_or_reported()
    {
        if (!RequireServer() || !RequireKey() || RetriesInsideATransaction is not bool retries) return;
        var store = Store<EntityTranslationModel>();
        await store.InitAsync();
        var entity = Guid.NewGuid();
        // The other writer commits on its own connection, outside the caller's boundary.
        var racing = new RacingStore<EntityTranslationModel>(store, () =>
        {
            Task other;
            using (ExecutionContext.SuppressFlow())
            {
                other = Task.Run(async () => await Store<EntityTranslationModel>().CreateAsync(Row(entity, "other writer")));
            }
            return other;
        });

        var act = async () =>
        {
            await using var uow = SqlUnitOfWork.FromStore(store);
            await uow.BeginAsync();
            await LocalizedTranslationRows.SaveAsync(racing, entity, "Product", "sk",
                new Dictionary<string, string?> { ["Name"] = "ours" }, CancellationToken.None);
            await uow.CommitAsync();
        };

        if (retries)
        {
            await act.Should().NotThrowAsync();
            (await store.ReadAsync(t => t.EntityGuid == entity, null, null, null)).Select(r => r.Value).Should().Equal("ours");
        }
        else
        {
            await act.Should().ThrowAsync<StoreUniqueViolationException>();
            (await store.ReadAsync(t => t.EntityGuid == entity, null, null, null)).Select(r => r.Value).Should().Equal("other writer");
        }
        racing.Raced.Should().BeTrue();
    }

    [Fact]
    public async Task Schema_ensure_creates_the_value_hash_index()
    {
        if (!RequireServer()) return;
        var store = Store<EntityTranslationModel>();
        await store.InitAsync();
        var table = EntityTranslationMapping<EntityTranslationModel>.DefaultTable;

        var info = await Indexes(store.Connector!).GetInfoAsync(EntityTranslationMapping<EntityTranslationModel>.ValueHashIndexName(table), table);

        info.Should().NotBeNull();
        info!.Unique.Should().BeFalse();
        info.Fields.Select(f => f.Name.ToLowerInvariant()).Should().Equal("valuehash");
    }

    [Fact]
    public async Task A_filter_on_a_translated_field_falls_back_and_looks_up_by_hash_through_the_providers_translator()
    {
        if (!RequireServer()) return;
        var context = new Context { CurrentCulture = new CultureInfo("sk"), DefaultCulture = new CultureInfo("en") };
        var products = Store<SqlProduct>();
        var translations = Store<EntityTranslationModel>();
        var run = Guid.NewGuid().ToString("N")[..8];
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<SqlProduct>, SqlProduct>(products, translations, context);
        context.CurrentCulture = new CultureInfo("en");
        var a = await bulk.CreateAsync(new SqlProduct { Name = $"Chair-{run}", Code = run });
        var b = await bulk.CreateAsync(new SqlProduct { Name = $"Lavica-{run}", Code = run });
        var c = await bulk.CreateAsync(new SqlProduct { Name = $"Stolicka-{run}", Code = run });
        context.CurrentCulture = new CultureInfo("sk");
        await translations.CreateAsync(new EntityTranslationModel { EntityGuid = a, EntityType = nameof(SqlProduct), FieldName = "Name", Culture = "sk", Value = $"Stolicka-{run}" });
        await translations.CreateAsync(new EntityTranslationModel { EntityGuid = c, EntityType = nameof(SqlProduct), FieldName = "Name", Culture = "sk", Value = $"Kreslo-{run}" });

        async Task<List<Guid?>> Find(Expression<Func<SqlProduct, bool>> filter)
            => (await bulk.ReadAsync(filter, null, null, null)).Select(x => x.Guid).ToList();

        (await Find(x => x.Code == run && x.Name == $"Stolicka-{run}")).Should().Equal(a);
        (await Find(x => x.Code == run && x.Name == $"Lavica-{run}")).Should().Equal(b);
        (await Find(x => x.Code == run && x.Name == $"Chair-{run}")).Should().BeEmpty();
        var both = new List<string> { $"Stolicka-{run}", $"Lavica-{run}" };
        (await Find(x => x.Code == run && both.Contains(x.Name))).Should().BeEquivalentTo(new Guid?[] { a, b });
        (await Find(x => x.Code == run && x.Name.Contains("avica"))).Should().Equal(b);
        (await bulk.CountAsync(x => x.Code == run && x.Name != $"Stolicka-{run}")).Should().Be(2);
    }

    [Fact]
    public async Task A_translation_row_stored_without_its_hash_is_still_found()
    {
        if (!RequireServer()) return;
        var context = new Context { CurrentCulture = new CultureInfo("sk"), DefaultCulture = new CultureInfo("en") };
        var products = Store<SqlProduct>();
        var translations = Store<TenantTranslation>();
        var run = Guid.NewGuid().ToString("N")[..8];
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<SqlProduct>, SqlProduct, TenantTranslation>(products, translations, context);
        var guid = Guid.NewGuid();
        await products.CreateAsync(new SqlProduct { Guid = guid, Name = $"Stolicka-{run}", Code = run });
        // Written around the wrappers: the model stores its hash as a plain property, so this row has none.
        await translations.CreateAsync(new TenantTranslation
        {
            EntityGuid = guid, EntityType = nameof(SqlProduct), FieldName = "Name", Culture = "sk", Value = $"Kreslo-{run}",
        });

        (await bulk.CountAsync(x => x.Code == run && x.Name == $"Kreslo-{run}")).Should().Be(1);
        (await bulk.CountAsync(x => x.Code == run && x.Name == $"Stolicka-{run}")).Should().Be(0);
    }

    [Fact]
    public async Task A_batched_read_renders_past_one_batch_and_returns_every_row()
    {
        if (!RequireServer()) return;
        var store = Store<EntityTranslationModel>();
        var entities = Enumerable.Range(0, LocalizedTranslationRows.ReadBatchSize * 2 + 1).Select(_ => Guid.NewGuid()).ToList();
        await store.CreateAsync(entities.Select(e => Row(e, $"v-{e:N}")).ToList());

        var rows = await LocalizedTranslationRows.ReadManyAsync(store, entities, "Product", "sk", CancellationToken.None);

        rows.Should().HaveCount(entities.Count);
        rows[entities[^1]]["Name"].Should().Be($"v-{entities[^1]:N}");
    }

    [Fact]
    public async Task A_consumer_model_maps_with_its_own_key_and_serves_the_wrappers()
    {
        if (!RequireServer() || !RequireKey()) return;
        var translations = Store<TenantTranslation>();
        await translations.InitAsync();
        (await KeyColumnsAsync(translations, TestModels.TenantTable)).Should().Equal("entityguid", "entitytype", "fieldname", "culture");

        var context = new Context { CurrentCulture = new CultureInfo("sk"), DefaultCulture = new CultureInfo("en") };
        var products = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<Product>, Product, TenantTranslation>(
            new AsyncInMemoryStore<Product>(), translations, context);

        var guid = await products.CreateAsync(new Product { Name = "Stolicka" });

        (await products.ReadAsync(guid))!.Name.Should().Be("Stolicka");
        (await translations.CountAsync(t => t.EntityGuid == guid)).Should().Be(1);
    }

    [Fact]
    public async Task A_save_costs_one_read_and_one_call_of_each_kind()
    {
        if (!RequireServer()) return;
        var inner = Store<EntityTranslationModel>();
        var entity = Guid.NewGuid();
        await inner.CreateAsync(new[] { Row(entity, "stare"), Row(entity, "zmaz ma", field: "Description") });
        var counted = new RacingStore<EntityTranslationModel>(inner, race: null);

        await LocalizedTranslationRows.SaveAsync(counted, entity, "Product", "sk",
            new Dictionary<string, string?> { ["Name"] = "nove", ["Description"] = null, ["Code"] = "pridane" }, CancellationToken.None);

        (counted.Reads, counted.Creates, counted.Updates, counted.Deletes).Should().Be((1, 1, 1, 1));
        (await inner.ReadAsync(t => t.EntityGuid == entity, null, null, null))
            .Select(r => (r.FieldName, r.Value, r.ValueHash)).OrderBy(r => r.FieldName)
            .Should().Equal(("Code", "pridane", LocalizedValueHash.Of("pridane")), ("Name", "nove", LocalizedValueHash.Of("nove")));
    }

    [Fact]
    public async Task A_racing_writer_on_one_key_of_a_batch_leaves_one_row_per_key()
    {
        if (!RequireServer() || !RequireKey()) return;
        var inner = Store<EntityTranslationModel>();
        var entity = Guid.NewGuid();
        // The other writer stores one of the two keys after our read: the batch's create is refused as a whole.
        var racing = new RacingStore<EntityTranslationModel>(inner, () => inner.CreateAsync(Row(entity, "other writer")));

        await LocalizedTranslationRows.SaveAsync(racing, entity, "Product", "sk",
            new Dictionary<string, string?> { ["Name"] = "ours", ["Description"] = "popis" }, CancellationToken.None);

        racing.Raced.Should().BeTrue("the test must actually have raced");
        (await inner.ReadAsync(t => t.EntityGuid == entity, null, null, null))
            .Select(r => (r.FieldName, r.Value)).OrderBy(r => r.FieldName)
            .Should().Equal(("Description", "popis"), ("Name", "ours"));
    }

    [Fact]
    public async Task Deleting_many_entities_and_one_culture_runs_through_the_providers_translator()
    {
        if (!RequireServer()) return;
        var store = Store<EntityTranslationModel>();
        var gone = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var oneCulture = Guid.NewGuid();
        var kept = Guid.NewGuid();
        foreach (var entity in gone.Append(oneCulture).Append(kept))
        {
            var czech = Row(entity, "cs");
            czech.Culture = "cs";
            await store.CreateAsync(new[] { Row(entity, "sk"), czech });
        }

        await LocalizedTranslationRows.DeleteManyAsync(store, gone, "Product", CancellationToken.None);
        await LocalizedTranslationRows.DeleteCultureAsync(store, oneCulture, "Product", "sk", CancellationToken.None);

        var left = (await store.ReadAsync(t => gone.Contains(t.EntityGuid) || t.EntityGuid == oneCulture || t.EntityGuid == kept, null, null, null))
            .Select(r => (r.EntityGuid, r.Culture)).ToList();
        left.Should().BeEquivalentTo(new[] { (oneCulture, "cs"), (kept, "sk"), (kept, "cs") });
    }

    [Fact]
    public async Task The_wrappers_collection_filter_and_delete_writes_reach_the_provider_batched()
    {
        if (!RequireServer()) return;
        var context = new Context { CurrentCulture = new CultureInfo("sk"), DefaultCulture = new CultureInfo("en") };
        var products = Store<SqlProduct>();
        var counted = new RacingStore<EntityTranslationModel>(Store<EntityTranslationModel>(), race: null);
        var run = Guid.NewGuid().ToString("N")[..8];
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<SqlProduct>, SqlProduct>(products, counted, context);
        var items = Enumerable.Range(0, 3).Select(i => new SqlProduct { Guid = Guid.NewGuid(), Name = $"sk-{i}", Code = run }).ToList();

        await bulk.CreateAsync(items);
        (counted.Reads, counted.Creates).Should().Be((1, 1));

        await bulk.UpdateAsync(x => x.Code == run, x => x.Name += "!");
        (await bulk.ReadAsync(x => x.Code == run, null, null, null)).Select(x => x.Name)
            .Should().BeEquivalentTo("sk-0!", "sk-1!", "sk-2!");
        (await products.ReadAsync(x => x.Code == run, null, null, null)).Select(x => x.Name)
            .Should().BeEquivalentTo(new[] { "sk-0", "sk-1", "sk-2" }, "the base column keeps what the create stored");

        var deletes = counted.Deletes;
        await bulk.DeleteAsync(x => x.Code == run);
        (counted.Deletes - deletes).Should().Be(1);
        var guids = items.Select(i => i.Guid!.Value).ToList();
        (await counted.CountAsync(t => guids.Contains(t.EntityGuid))).Should().Be(0);
    }

    /// <summary>TASK-682: three products in SQL, one per fallback case, values unique to this run.</summary>
    private async Task<(EntityTranslations<SqlProduct> Api, RacingStore<SqlProduct> Products, RacingStore<EntityTranslationModel> Translations,
        Guid Translated, Guid Untranslated, Guid Other, string Run)> ApiFixtureAsync()
    {
        var context = new Context { CurrentCulture = new CultureInfo("en"), DefaultCulture = new CultureInfo("en") };
        var products = new RacingStore<SqlProduct>(Store<SqlProduct>(), race: null);
        var translations = new RacingStore<EntityTranslationModel>(Store<EntityTranslationModel>(), race: null);
        var run = Guid.NewGuid().ToString("N")[..8];
        var translated = await products.CreateAsync(new SqlProduct { Name = $"chair-{run}", Code = run });
        var untranslated = await products.CreateAsync(new SqlProduct { Name = $"lavica-{run}", Code = run });
        var other = await products.CreateAsync(new SqlProduct { Name = $"table-{run}", Code = run });
        var api = new EntityTranslations<SqlProduct>(products, translations, context);
        await api.SetAsync(translated, new CultureInfo("sk"), new Dictionary<string, string?> { ["Name"] = $"stolicka-{run}" });
        await api.SetAsync(other, new CultureInfo("sk"), new Dictionary<string, string?> { ["Name"] = $"stol-{run}" });
        await api.SetAsync(other, new CultureInfo("cs"), new Dictionary<string, string?> { ["Name"] = $"stul-{run}" });
        return (api, products, translations, translated, untranslated, other, run);
    }

    [Fact]
    public async Task The_explicit_api_reads_writes_and_removes_translations_on_the_provider()
    {
        if (!RequireServer()) return;
        var (api, _, translations, translated, untranslated, other, run) = await ApiFixtureAsync();
        var sk = new CultureInfo("sk");

        var reads = translations.Reads;
        var slovak = await api.GetAsync(new[] { translated, untranslated, other }, sk);
        (translations.Reads - reads).Should().Be(1);
        slovak.Keys.Should().BeEquivalentTo(new[] { translated, other });
        slovak[other]["Name"].Should().Be($"stol-{run}");

        (await api.GetAllCulturesAsync(other)).Keys.Should().BeEquivalentTo("sk", "cs");

        await api.SetAsync(other, sk, new Dictionary<string, string?> { ["Name"] = null });
        (await api.GetAllCulturesAsync(other)).Keys.Should().BeEquivalentTo("cs");

        await api.RemoveCultureAsync(other, new CultureInfo("cs"));
        await api.RemoveEntitiesAsync(new[] { translated });
        (await translations.CountAsync(t => t.EntityGuid == translated || t.EntityGuid == other)).Should().Be(0);
    }

    [Fact]
    public async Task The_explicit_api_finds_with_fallback_and_checks_uniqueness_on_the_provider()
    {
        if (!RequireServer()) return;
        var (api, products, translations, translated, untranslated, other, run) = await ApiFixtureAsync();
        var sk = new CultureInfo("sk");

        var before = (products.Reads, translations.Reads);
        (await api.FindEntityAsync("Name", sk, $"lavica-{run}")).Should().Be(untranslated);
        (products.Reads - before.Item1, translations.Reads - before.Item2).Should().Be((2, 2));

        (await api.FindEntityAsync("Name", sk, $"stolicka-{run}")).Should().Be(translated);
        (await api.FindEntityAsync("Name", sk, $"chair-{run}")).Should().BeNull("a translated product matches only on its translation");
        (await api.FindEntityAsync("Name", new CultureInfo("en"), $"chair-{run}")).Should().Be(translated);

        (await api.ExistsAsync("Name", sk, $"lavica-{run}")).Should().BeTrue();
        (await api.ExistsAsync("Name", sk, $"lavica-{run}", exceptEntityGuid: untranslated)).Should().BeFalse();
        (await api.ExistsAsync("Name", sk, $"stol-{run}", exceptEntityGuid: translated)).Should().BeTrue();
        (await api.ExistsAsync("Name", sk, $"stol-{run}", exceptEntityGuid: other)).Should().BeFalse();
    }

    [Fact]
    public async Task The_explicit_api_localizes_loaded_entities_with_fallback_on_the_provider()
    {
        if (!RequireServer()) return;
        var (api, products, translations, translated, untranslated, other, run) = await ApiFixtureAsync();
        var loaded = (await products.ReadAsync(x => x.Code == run, null, null, null)).OrderBy(x => x.Name).ToList();
        var reads = translations.Reads;

        var localized = await api.LocalizeAsync(loaded, new CultureInfo("sk"));

        (translations.Reads - reads).Should().Be(1);
        localized.Select(x => x.Name).Should().Equal($"stolicka-{run}", $"lavica-{run}", $"stol-{run}");
    }

    [Fact]
    public async Task A_provider_that_cannot_hold_the_key_reports_it()
    {
        if (!RequireServer() || HoldsTheKey) return;
        var store = Store<EntityTranslationModel>();

        await store.InitAsync();

        // Lazy schema-ensure degrades and reports (rule 49): the store works, the missing key is on the record.
        store.Connector!.IndexCreationFailures.Select(f => f.IndexName)
            .Should().Contain(EntityTranslationMapping<EntityTranslationModel>.KeyIndexName(EntityTranslationMapping<EntityTranslationModel>.DefaultTable));
    }

    private sealed class Context : IEntityLocalizationContext
    {
        public CultureInfo CurrentCulture { get; set; } = CultureInfo.InvariantCulture;
        public CultureInfo DefaultCulture { get; set; } = CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// Delegates to a real store; the first filtered collection read runs, then lets <c>race</c> store a row, and
    /// returns what it read before — the moment another writer commits between our read and our create.
    /// </summary>
    private sealed class RacingStore<T> : IAsyncBulkStore<T> where T : AbstractModel
    {
        private readonly IAsyncBulkStore<T> _inner;
        private Func<Task>? _race;

        public RacingStore(IAsyncBulkStore<T> inner, Func<Task>? race)
        {
            _inner = inner;
            _race = race;
        }

        public bool Raced { get; private set; }

        /// <summary>Calls made through this store, one per call whatever its size (TASK-690 counts them).</summary>
        public int Reads { get; private set; }
        public int Creates { get; private set; }
        public int Updates { get; private set; }
        public int Deletes { get; private set; }

        public async Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            Reads++;
            var result = (await _inner.ReadAsync(filter, orderBy, limit, offset, ct)).ToList();
            var race = _race;
            if (race != null)
            {
                _race = null;
                await race();
                Raced = true;
            }
            return result;
        }

        public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => _inner.ReadAsync(guid, ct);
        public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => ((IAsyncReadStore<T>)_inner).ReadAsync(filter, ct);
        public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default) => _inner.ReadAsync(ct);
        public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _inner.CountAsync(filter, ct);
        public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) { Creates++; return _inner.CreateAsync(data, processDelegate, ct); }
        public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) { Creates++; return _inner.CreateAsync(data, storeDelegate, ct); }
        public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) { Updates++; return _inner.UpdateAsync(data, processDelegate, ct); }
        public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) { Updates++; return _inner.UpdateAsync(data, storeDelegate, ct); }
        public Task DeleteAsync(T data, CancellationToken ct = default) { Deletes++; return _inner.DeleteAsync(data, ct); }
        public Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default) => _inner.UpdateAsync(filter, updateAction, ct);
        public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default) => _inner.UpdateAsync(filter, updates, ct);
        public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default) { Deletes++; return _inner.DeleteAsync(data, ct); }
        public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default) { Deletes++; return _inner.DeleteAsync(filter, ct); }
        public Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) => _inner.SaveAsync(data, processDelegate, ct);
        public Task InitAsync(CancellationToken ct = default) => _inner.InitAsync(ct);
        public Task DestroyAsync(CancellationToken ct = default) => _inner.DestroyAsync(ct);
        public T CreateInstance() => _inner.CreateInstance();
    }
}

/// <summary>
/// Applies the mappings once per process. The registry writes process-wide state (table names, field flags, index
/// declarations) without synchronisation, so the gate lives here, in a non-generic class: a static on
/// <see cref="TranslationTableTests{DB}"/> would be one gate per closed type, and the providers' classes run in parallel.
/// </summary>
internal static class TranslationMappings
{
    private static readonly object Gate = new();
    private static bool _applied;

    public static void EnsureApplied()
    {
        lock (Gate)
        {
            if (_applied)
            {
                return;
            }
            var registry = LocalizationSqlMappings.Register(new ModelMapRegistry());
            LocalizationSqlMappings.Register<TenantTranslation>(registry, TestModels.TenantTable);
            registry.Register(new SqlProductMapping());
            registry.ApplyToDatabase();
            _applied = true;
        }
    }
}
