using System.Globalization;
using System.Linq.Expressions;
using Birko.Configuration;
using Birko.Data.Exceptions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.SQL.Mappings;
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
}

public sealed class Product : AbstractModel, ILocalizable
{
    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
}

public static class TestModels
{
    public const string TenantTable = "BirkoTestTenantTranslations";
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

        public RacingStore(IAsyncBulkStore<T> inner, Func<Task> race)
        {
            _inner = inner;
            _race = race;
        }

        public bool Raced { get; private set; }

        public async Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
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
        public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) => _inner.CreateAsync(data, processDelegate, ct);
        public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) => _inner.CreateAsync(data, storeDelegate, ct);
        public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) => _inner.UpdateAsync(data, processDelegate, ct);
        public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) => _inner.UpdateAsync(data, storeDelegate, ct);
        public Task DeleteAsync(T data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
        public Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default) => _inner.UpdateAsync(filter, updateAction, ct);
        public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default) => _inner.UpdateAsync(filter, updates, ct);
        public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
        public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default) => _inner.DeleteAsync(filter, ct);
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
            registry.ApplyToDatabase();
            _applied = true;
        }
    }
}
