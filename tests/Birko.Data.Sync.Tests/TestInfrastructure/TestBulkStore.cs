using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Sync.Tests.TestInfrastructure;

public class TestBulkStore : AbstractBulkStore<TestSyncModel>
{
    private readonly Dictionary<Guid, TestSyncModel> _data = new();

    public override long Count(Expression<Func<TestSyncModel, bool>>? filter = null)
    {
        if (filter == null) return _data.Count;
        return _data.Values.AsQueryable().Count(filter);
    }

    public override TestSyncModel? Read(Guid guid) => _data.GetValueOrDefault(guid);

    public override TestSyncModel? Read(Expression<Func<TestSyncModel, bool>>? filter = null) =>
        filter == null ? _data.Values.FirstOrDefault() : _data.Values.AsQueryable().FirstOrDefault(filter);

    public override IEnumerable<TestSyncModel> Read() => _data.Values.ToList();

    public override IEnumerable<TestSyncModel> Read(Expression<Func<TestSyncModel, bool>>? filter = null, OrderBy<TestSyncModel>? orderBy = null, int? limit = null, int? offset = null)
    {
        IEnumerable<TestSyncModel> result = _data.Values;
        if (filter != null) result = result.AsQueryable().Where(filter);
        return result.ToList();
    }

    public override Guid Create(TestSyncModel data, StoreDataDelegate<TestSyncModel>? storeDelegate = null)
    {
        data.Guid ??= Guid.NewGuid();
        _data[data.Guid.Value] = data;
        return data.Guid.Value;
    }

    public override void Create(IEnumerable<TestSyncModel> data, StoreDataDelegate<TestSyncModel>? storeDelegate = null)
    {
        foreach (var item in data) Create(item, storeDelegate);
    }

    public override void Update(TestSyncModel data, StoreDataDelegate<TestSyncModel>? storeDelegate = null)
    {
        if (data.Guid.HasValue) _data[data.Guid.Value] = data;
    }

    public override void Update(IEnumerable<TestSyncModel> data, StoreDataDelegate<TestSyncModel>? storeDelegate = null)
    {
        foreach (var item in data) Update(item, storeDelegate);
    }

    public override void Update(Expression<Func<TestSyncModel, bool>> filter, Action<TestSyncModel> updateAction)
    {
        var matches = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in matches) updateAction(item);
    }

    public override void Update(Expression<Func<TestSyncModel, bool>> filter, PropertyUpdate<TestSyncModel> updates) { }

    public override void Delete(TestSyncModel data)
    {
        if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
    }

    public override void Delete(IEnumerable<TestSyncModel> data)
    {
        foreach (var item in data) Delete(item);
    }

    public override void Delete(Expression<Func<TestSyncModel, bool>> filter)
    {
        var toDelete = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in toDelete) Delete(item);
    }

    public override void Init() { }
    public override void Destroy() { }
    public override TestSyncModel CreateInstance() => new();

    public override Guid Save(TestSyncModel data, StoreDataDelegate<TestSyncModel>? storeDelegate = null)
    {
        if (data.Guid == null || data.Guid == Guid.Empty) return Create(data, storeDelegate);
        Update(data, storeDelegate);
        return data.Guid.Value;
    }

    public Dictionary<Guid, TestSyncModel> Data => _data;
}
