using System;
using System.IO;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>A row for TASK-555's update-miss contract.</summary>
[Table("UpdateMissRows")]
public class UpdateMissRow : AbstractModel
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>TASK-555's cross-provider update-miss contract on <see cref="SQLiteStore{T}"/>, against a real file.</summary>
public sealed class SQLiteUpdateMissContractTests : StoreContract<UpdateMissRow>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-555-" + Guid.NewGuid().ToString("N"));

    public SQLiteUpdateMissContractTests() => Directory.CreateDirectory(_root);

    protected override IBulkStore<UpdateMissRow> NewStore()
    {
        var store = new SQLiteStore<UpdateMissRow>();
        store.SetSettings(new SqLiteSettings(_root, $"sync-{Guid.NewGuid():N}.db"));
        return store;
    }

    protected override UpdateMissRow NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissRow entity) => entity.Name;
    protected override void Rename(UpdateMissRow entity, string name) => entity.Name = name;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}

/// <summary>The async twin, on <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class AsyncSQLiteUpdateMissContractTests : AsyncStoreContract<UpdateMissRow>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-555-" + Guid.NewGuid().ToString("N"));

    public AsyncSQLiteUpdateMissContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<UpdateMissRow> NewStore()
    {
        var store = new AsyncSQLiteStore<UpdateMissRow>();
        store.SetSettings(new SqLiteSettings(_root, $"async-{Guid.NewGuid():N}.db"));
        return store;
    }

    protected override UpdateMissRow NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissRow entity) => entity.Name;
    protected override void Rename(UpdateMissRow entity, string name) => entity.Name = name;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
