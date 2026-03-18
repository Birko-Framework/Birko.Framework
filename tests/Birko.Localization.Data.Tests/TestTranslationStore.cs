using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Localization.Data;

namespace Birko.Localization.Data.Tests;

/// <summary>
/// In-memory async bulk store for TranslationModel, used in tests.
/// </summary>
internal class TestTranslationStore : AbstractAsyncBulkStore<TranslationModel>
{
    private readonly List<TranslationModel> _data = new();

    public void Seed(IEnumerable<TranslationModel> items) => _data.AddRange(items);

    public override Task<long> CountAsync(Expression<Func<TranslationModel, bool>>? filter = null, CancellationToken ct = default)
    {
        long count = filter == null ? _data.Count : _data.AsQueryable().Count(filter);
        return Task.FromResult(count);
    }

    public override Task<IEnumerable<TranslationModel>> ReadAsync(Expression<Func<TranslationModel, bool>>? filter = null, OrderBy<TranslationModel>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        IEnumerable<TranslationModel> query = _data;
        if (filter != null) query = query.AsQueryable().Where(filter);
        if (offset.HasValue) query = query.Skip(offset.Value);
        if (limit.HasValue) query = query.Take(limit.Value);
        return Task.FromResult(query);
    }

    public override Task<IEnumerable<TranslationModel>> ReadAsync(CancellationToken ct = default) => ReadAsync(null, null, null, null, ct);
    public override Task<TranslationModel?> ReadAsync(Guid guid, CancellationToken ct = default) => Task.FromResult(_data.FirstOrDefault(x => x.Guid == guid));
    public override Task<TranslationModel?> ReadAsync(Expression<Func<TranslationModel, bool>>? filter = null, CancellationToken ct = default) => Task.FromResult(filter == null ? _data.FirstOrDefault() : _data.AsQueryable().FirstOrDefault(filter));
    public override Task<Guid> CreateAsync(TranslationModel data, StoreDataDelegate<TranslationModel>? processDelegate = null, CancellationToken ct = default) { data.Guid ??= Guid.NewGuid(); _data.Add(data); return Task.FromResult(data.Guid.Value); }
    public override Task CreateAsync(IEnumerable<TranslationModel> data, StoreDataDelegate<TranslationModel>? storeDelegate = null, CancellationToken ct = default) { _data.AddRange(data); return Task.CompletedTask; }
    public override Task UpdateAsync(TranslationModel data, StoreDataDelegate<TranslationModel>? processDelegate = null, CancellationToken ct = default) => Task.CompletedTask;
    public override Task UpdateAsync(IEnumerable<TranslationModel> data, StoreDataDelegate<TranslationModel>? storeDelegate = null, CancellationToken ct = default) => Task.CompletedTask;
    public override Task DeleteAsync(TranslationModel data, CancellationToken ct = default) { _data.Remove(data); return Task.CompletedTask; }
    public override Task DeleteAsync(IEnumerable<TranslationModel> data, CancellationToken ct = default) { foreach (var d in data) _data.Remove(d); return Task.CompletedTask; }
    public override Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public override Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
    public override TranslationModel CreateInstance() => new();
    public override Task<Guid> SaveAsync(TranslationModel data, StoreDataDelegate<TranslationModel>? processDelegate = null, CancellationToken ct = default) => CreateAsync(data, processDelegate, ct);
}
