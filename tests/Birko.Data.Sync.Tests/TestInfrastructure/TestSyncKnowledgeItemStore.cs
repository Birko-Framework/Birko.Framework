using Birko.Data.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Sync.Tests.TestInfrastructure;

public class TestSyncKnowledgeItemStore : AbstractBulkStore<TestSyncKnowledge>, ISyncKnowledgeItemStore<TestSyncKnowledge>
{
    private readonly Dictionary<Guid, TestSyncKnowledge> _data = new();
    private readonly Dictionary<string, DateTime?> _syncTimes = new();

    protected override long CountCore(Expression<Func<TestSyncKnowledge, bool>>? filter = null)
    {
        if (filter == null) return _data.Count;
        return _data.Values.AsQueryable().Count(filter);
    }

    public override TestSyncKnowledge? Read(Guid guid) => _data.GetValueOrDefault(guid);

    protected override TestSyncKnowledge? ReadCore(Expression<Func<TestSyncKnowledge, bool>>? filter = null) =>
        filter == null ? _data.Values.FirstOrDefault() : _data.Values.AsQueryable().FirstOrDefault(filter);

    public override IEnumerable<TestSyncKnowledge> Read() => _data.Values.ToList();

    protected override IEnumerable<TestSyncKnowledge> ReadCore(Expression<Func<TestSyncKnowledge, bool>>? filter = null, OrderBy<TestSyncKnowledge>? orderBy = null, int? limit = null, int? offset = null)
    {
        IEnumerable<TestSyncKnowledge> result = _data.Values;
        if (filter != null) result = result.AsQueryable().Where(filter);
        return result.ToList();
    }

    protected override Guid CreateCore(TestSyncKnowledge data, StoreDataDelegate<TestSyncKnowledge>? storeDelegate = null)
    {
        data.Guid ??= Guid.NewGuid();
        _data[data.Guid.Value] = data;
        return data.Guid.Value;
    }

    protected override void CreateCore(IEnumerable<TestSyncKnowledge> data, StoreDataDelegate<TestSyncKnowledge>? storeDelegate = null)
    {
        foreach (var item in data) Create(item, storeDelegate);
    }

    protected override void UpdateCore(TestSyncKnowledge data, StoreDataDelegate<TestSyncKnowledge>? storeDelegate = null)
    {
        if (data.Guid.HasValue) _data[data.Guid.Value] = data;
    }

    protected override void UpdateCore(IEnumerable<TestSyncKnowledge> data, StoreDataDelegate<TestSyncKnowledge>? storeDelegate = null)
    {
        foreach (var item in data)
        {
            if (item.Guid.HasValue)
                _data[item.Guid.Value] = item;
            else
                Create(item, storeDelegate);
        }
    }

    public override void Update(Expression<Func<TestSyncKnowledge, bool>> filter, Action<TestSyncKnowledge> updateAction)
    {
        var matches = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in matches) updateAction(item);
    }

    public override void Update(Expression<Func<TestSyncKnowledge, bool>> filter, PropertyUpdate<TestSyncKnowledge> updates) { }

    protected override void DeleteCore(TestSyncKnowledge data)
    {
        if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
    }

    protected override void DeleteCore(IEnumerable<TestSyncKnowledge> data)
    {
        foreach (var item in data) Delete(item);
    }

    public override void Delete(Expression<Func<TestSyncKnowledge, bool>> filter)
    {
        var toDelete = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in toDelete) Delete(item);
    }

    protected override void InitCore() { }
    public override void Destroy() { }
    public override TestSyncKnowledge CreateInstance() => new();

    public override Guid Save(TestSyncKnowledge data, StoreDataDelegate<TestSyncKnowledge>? storeDelegate = null)
    {
        if (data.Guid == null || data.Guid == Guid.Empty) return Create(data, storeDelegate);
        Update(data, storeDelegate);
        return data.Guid.Value;
    }

    public DateTime? GetLastSyncTime(string scope)
    {
        return _syncTimes.GetValueOrDefault(scope);
    }

    public DateTime? SetLastSyncTime(string scope, DateTime? lastSyncTime)
    {
        _syncTimes[scope] = lastSyncTime;
        return lastSyncTime;
    }

    public TestSyncKnowledge CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options)
    {
        return new TestSyncKnowledge
        {
            EntityGuid = guid,
            Scope = options.Scope,
            LastSyncedAt = DateTime.UtcNow,
            LocalVersion = localItemHash,
            RemoteVersion = remoteItemHash,
            IsLocalDeleted = string.IsNullOrEmpty(localItemHash),
            IsRemoteDeleted = string.IsNullOrEmpty(remoteItemHash)
        };
    }
}
