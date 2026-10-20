using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.LocalizationContract;

/// <summary>
/// TASK-698, per provider: two writers saving the same translation key at once leave one row. Before, only SQL's
/// unique key refused the second create (TASK-680), so every other provider stored a duplicate row and the upsert's
/// retry never fired. A row's Guid is now derived from its key, and every store refuses a second create of a stored
/// Guid (the store contract), so the same retry runs everywhere.
/// <para>Observed state: the rows stored for the key, read back from the store.</para>
/// </summary>
public abstract class TranslationRaceContract
{
    /// <summary>False when the backend is not reachable; a live provider reports its own skip.</summary>
    protected virtual bool Ready() => true;

    /// <summary>
    /// Two stores over ONE database, as two writers would hold them. An in-process store whose instances do not share
    /// data returns the same instance twice.
    /// </summary>
    protected abstract (IAsyncBulkStore<EntityTranslationModel> Ours, IAsyncBulkStore<EntityTranslationModel> Theirs) NewTranslationStores();

    /// <summary>Makes writes visible to queries before the test reads them back (a near-real-time index).</summary>
    protected virtual Task SettleAsync(IAsyncBulkStore<EntityTranslationModel> store) => Task.CompletedTask;

    private static Dictionary<string, string?> Values(string value) => new() { ["Name"] = value };

    private static async Task<List<EntityTranslationModel>> RowsAsync(IAsyncBulkStore<EntityTranslationModel> store, Guid entity)
        => (await store.ReadAsync(t => t.EntityGuid == entity, null, null, null)).ToList();

    [Fact]
    public async Task Translation_A_racing_save_of_one_key_leaves_one_row()
    {
        if (!Ready()) return;
        var (ours, theirs) = NewTranslationStores();
        var entity = Guid.NewGuid();
        var racing = new RacingStore(ours, () => LocalizedTranslationRows.SaveAsync(theirs, entity, "Product", "sk", Values("theirs"), CancellationToken.None));

        Exception? thrown = null;
        try
        {
            await LocalizedTranslationRows.SaveAsync(racing, entity, "Product", "sk", Values("ours"), CancellationToken.None);
        }
        catch (StoreUniqueViolationException refusal)
        {
            thrown = refusal;
        }

        racing.Raced.Should().BeTrue("the test must actually have raced");
        await SettleAsync(ours);
        var rows = await RowsAsync(ours, entity);
        rows.Should().ContainSingle("two writers of one key must not store two rows");
        // On a near-real-time index (ElasticSearch) the retry sees the other writer's row only through a realtime read
        // by its KeyGuid: the search missed it inside the refresh window (measured, TASK-698).
        thrown.Should().BeNull();
        rows[0].Value.Should().Be("ours", "the retry updates the other writer's row");
    }

    [Fact]
    public async Task Translation_A_created_row_is_stored_under_its_key_guid()
    {
        if (!Ready()) return;
        var (ours, _) = NewTranslationStores();
        var entity = Guid.NewGuid();

        await LocalizedTranslationRows.SaveAsync(ours, entity, "Product", "sk", Values("value"), CancellationToken.None);

        await SettleAsync(ours);
        (await RowsAsync(ours, entity)).Single().Guid.Should().Be(LocalizedTranslationRows.KeyGuid(entity, "Product", "Name", "sk"));
    }

    [Fact]
    public async Task Translation_A_key_removed_and_saved_again_is_stored_once()
    {
        if (!Ready()) return;
        var (ours, _) = NewTranslationStores();
        var entity = Guid.NewGuid();

        await LocalizedTranslationRows.SaveAsync(ours, entity, "Product", "sk", Values("first"), CancellationToken.None);
        await SettleAsync(ours);
        await LocalizedTranslationRows.SaveAsync(ours, entity, "Product", "sk", new Dictionary<string, string?> { ["Name"] = null }, CancellationToken.None);
        await SettleAsync(ours);
        await LocalizedTranslationRows.SaveAsync(ours, entity, "Product", "sk", Values("second"), CancellationToken.None);

        await SettleAsync(ours);
        (await RowsAsync(ours, entity)).Select(r => r.Value).Should().Equal("second");
    }

    [Fact]
    public async Task Translation_A_key_held_outside_the_stores_scope_is_refused_and_named()
    {
        if (!Ready()) return;
        var (ours, theirs) = NewTranslationStores();
        var entity = Guid.NewGuid();
        // Another scope (another tenant) already translated this shared entity.
        await LocalizedTranslationRows.SaveAsync(theirs, entity, "Product", "sk", Values("theirs"), CancellationToken.None);
        await SettleAsync(theirs);
        var scoped = new ScopedStore(ours);

        var save = () => LocalizedTranslationRows.SaveAsync(scoped, entity, "Product", "sk", Values("ours"), CancellationToken.None);

        // Decided by the user (TASK-698): scopes must match; the refusal names the mismatch, and the hidden row is
        // neither read through a realtime read beneath the decorator nor updated.
        (await save.Should().ThrowAsync<StoreUniqueViolationException>()).WithMessage("*scoped narrower than its entities*");
        await SettleAsync(ours);
        (await RowsAsync(ours, entity)).Select(r => r.Value).Should().Equal("theirs");
    }

    /// <summary>
    /// Hides every row it did not create, as a tenant decorator hides another tenant's rows. It does not forward the
    /// realtime read: a scoping decorator must not.
    /// </summary>
    private sealed class ScopedStore : IAsyncBulkStore<EntityTranslationModel>, IStoreWrapper
    {
        private readonly IAsyncBulkStore<EntityTranslationModel> _inner;
        private readonly HashSet<Guid> _mine = new();

        public ScopedStore(IAsyncBulkStore<EntityTranslationModel> inner) => _inner = inner;
        public object? GetInnerStore() => _inner;

        private IEnumerable<EntityTranslationModel> Visible(IEnumerable<EntityTranslationModel> rows) => rows.Where(r => r.Guid is Guid g && _mine.Contains(g));

        public async Task<IEnumerable<EntityTranslationModel>> ReadAsync(Expression<Func<EntityTranslationModel, bool>>? filter = null, OrderBy<EntityTranslationModel>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
            => Visible(await _inner.ReadAsync(filter, orderBy, limit, offset, ct)).ToList();
        public async Task<EntityTranslationModel?> ReadAsync(Guid guid, CancellationToken ct = default)
            => _mine.Contains(guid) ? await _inner.ReadAsync(guid, ct) : null;
        public async Task<EntityTranslationModel?> ReadAsync(Expression<Func<EntityTranslationModel, bool>>? filter = null, CancellationToken ct = default)
            => Visible(await _inner.ReadAsync(filter, null, null, null, ct)).FirstOrDefault();
        public async Task<IEnumerable<EntityTranslationModel>> ReadAsync(CancellationToken ct = default) => Visible(await _inner.ReadAsync(ct)).ToList();
        public async Task<long> CountAsync(Expression<Func<EntityTranslationModel, bool>>? filter = null, CancellationToken ct = default)
            => Visible(await _inner.ReadAsync(filter, null, null, null, ct)).LongCount();
        public async Task<Guid> CreateAsync(EntityTranslationModel data, StoreDataDelegate<EntityTranslationModel>? processDelegate = null, CancellationToken ct = default)
        {
            var guid = await _inner.CreateAsync(data, processDelegate, ct);
            _mine.Add(guid);
            return guid;
        }
        public async Task CreateAsync(IEnumerable<EntityTranslationModel> data, StoreDataDelegate<EntityTranslationModel>? storeDelegate = null, CancellationToken ct = default)
        {
            var items = data.ToList();
            await _inner.CreateAsync(items, storeDelegate, ct);
            foreach (var item in items) _mine.Add(item.Guid!.Value);
        }
        public Task UpdateAsync(EntityTranslationModel data, StoreDataDelegate<EntityTranslationModel>? processDelegate = null, CancellationToken ct = default) => _inner.UpdateAsync(data, processDelegate, ct);
        public Task UpdateAsync(IEnumerable<EntityTranslationModel> data, StoreDataDelegate<EntityTranslationModel>? storeDelegate = null, CancellationToken ct = default) => _inner.UpdateAsync(data, storeDelegate, ct);
        public Task DeleteAsync(EntityTranslationModel data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
        public Task UpdateAsync(Expression<Func<EntityTranslationModel, bool>> filter, Action<EntityTranslationModel> updateAction, CancellationToken ct = default) => _inner.UpdateAsync(filter, updateAction, ct);
        public Task UpdateAsync(Expression<Func<EntityTranslationModel, bool>> filter, PropertyUpdate<EntityTranslationModel> updates, CancellationToken ct = default) => _inner.UpdateAsync(filter, updates, ct);
        public Task DeleteAsync(IEnumerable<EntityTranslationModel> data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
        public Task DeleteAsync(Expression<Func<EntityTranslationModel, bool>> filter, CancellationToken ct = default) => _inner.DeleteAsync(filter, ct);
        public Task<Guid> SaveAsync(EntityTranslationModel data, StoreDataDelegate<EntityTranslationModel>? processDelegate = null, CancellationToken ct = default) => _inner.SaveAsync(data, processDelegate, ct);
        public Task InitAsync(CancellationToken ct = default) => _inner.InitAsync(ct);
        public Task DestroyAsync(CancellationToken ct = default) => _inner.DestroyAsync(ct);
        public EntityTranslationModel CreateInstance() => _inner.CreateInstance();
    }

    /// <summary>
    /// Delegates to a real store; right after its first collection read (the upsert's key read) it lets the other
    /// writer save, so the upsert's create meets that writer's row.
    /// </summary>
    private sealed class RacingStore : IAsyncBulkStore<EntityTranslationModel>, IStoreWrapper, IAsyncRealtimeReadStore<EntityTranslationModel>
    {
        // A decorator that does not scope reads forwards the realtime read; the upsert asks only the store it is given.
        public Task<EntityTranslationModel?> ReadRealtimeAsync(Guid guid, CancellationToken ct = default)
            => _inner is IAsyncRealtimeReadStore<EntityTranslationModel> realtime ? realtime.ReadRealtimeAsync(guid, ct) : _inner.ReadAsync(guid, ct);

        private readonly IAsyncBulkStore<EntityTranslationModel> _inner;
        private Func<Task>? _race;

        public RacingStore(IAsyncBulkStore<EntityTranslationModel> inner, Func<Task> race)
        {
            _inner = inner;
            _race = race;
        }

        public bool Raced { get; private set; }
        public object? GetInnerStore() => _inner;

        public async Task<IEnumerable<EntityTranslationModel>> ReadAsync(Expression<Func<EntityTranslationModel, bool>>? filter = null, OrderBy<EntityTranslationModel>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
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

        public Task<EntityTranslationModel?> ReadAsync(Guid guid, CancellationToken ct = default) => _inner.ReadAsync(guid, ct);
        public Task<EntityTranslationModel?> ReadAsync(Expression<Func<EntityTranslationModel, bool>>? filter = null, CancellationToken ct = default) => ((IAsyncReadStore<EntityTranslationModel>)_inner).ReadAsync(filter, ct);
        public Task<IEnumerable<EntityTranslationModel>> ReadAsync(CancellationToken ct = default) => _inner.ReadAsync(ct);
        public Task<long> CountAsync(Expression<Func<EntityTranslationModel, bool>>? filter = null, CancellationToken ct = default) => _inner.CountAsync(filter, ct);
        public Task<Guid> CreateAsync(EntityTranslationModel data, StoreDataDelegate<EntityTranslationModel>? processDelegate = null, CancellationToken ct = default) => _inner.CreateAsync(data, processDelegate, ct);
        public Task CreateAsync(IEnumerable<EntityTranslationModel> data, StoreDataDelegate<EntityTranslationModel>? storeDelegate = null, CancellationToken ct = default) => _inner.CreateAsync(data, storeDelegate, ct);
        public Task UpdateAsync(EntityTranslationModel data, StoreDataDelegate<EntityTranslationModel>? processDelegate = null, CancellationToken ct = default) => _inner.UpdateAsync(data, processDelegate, ct);
        public Task UpdateAsync(IEnumerable<EntityTranslationModel> data, StoreDataDelegate<EntityTranslationModel>? storeDelegate = null, CancellationToken ct = default) => _inner.UpdateAsync(data, storeDelegate, ct);
        public Task DeleteAsync(EntityTranslationModel data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
        public Task UpdateAsync(Expression<Func<EntityTranslationModel, bool>> filter, Action<EntityTranslationModel> updateAction, CancellationToken ct = default) => _inner.UpdateAsync(filter, updateAction, ct);
        public Task UpdateAsync(Expression<Func<EntityTranslationModel, bool>> filter, PropertyUpdate<EntityTranslationModel> updates, CancellationToken ct = default) => _inner.UpdateAsync(filter, updates, ct);
        public Task DeleteAsync(IEnumerable<EntityTranslationModel> data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
        public Task DeleteAsync(Expression<Func<EntityTranslationModel, bool>> filter, CancellationToken ct = default) => _inner.DeleteAsync(filter, ct);
        public Task<Guid> SaveAsync(EntityTranslationModel data, StoreDataDelegate<EntityTranslationModel>? processDelegate = null, CancellationToken ct = default) => _inner.SaveAsync(data, processDelegate, ct);
        public Task InitAsync(CancellationToken ct = default) => _inner.InitAsync(ct);
        public Task DestroyAsync(CancellationToken ct = default) => _inner.DestroyAsync(ct);
        public EntityTranslationModel CreateInstance() => _inner.CreateInstance();
    }
}
