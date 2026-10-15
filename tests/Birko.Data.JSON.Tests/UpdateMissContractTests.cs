using System;
using System.Collections.Generic;
using System.IO;
using Birko.Configuration;
using Birko.Data.JSON.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.JSON.Tests;

/// <summary>Creates a store per test in its own temp directory and removes them all afterwards.</summary>
internal sealed class TempDirs : IDisposable
{
    private readonly List<string> _dirs = new();

    public Settings NewSettings(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return new Settings(dir, "contract.json");
    }

    public void Dispose()
    {
        foreach (var d in _dirs)
        {
            try { Directory.Delete(d, recursive: true); } catch { }
        }
    }
}

/// <summary>TASK-555's cross-provider update-miss contract (<c>tests/Shared/StoreContract</c>) on <see cref="JsonStore{T}"/>.</summary>
public sealed class JsonUpdateMissContractTests : StoreContract<TestModel>, IDisposable
{
    protected override bool DuplicateCreateThrowsStoreException => false; // TASK-625
    private readonly TempDirs _dirs = new();

    protected override IBulkStore<TestModel> NewStore()
    {
        var store = new JsonStore<TestModel>();
        store.SetSettings(_dirs.NewSettings("birko-json-555-"));
        return store;
    }

    protected override TestModel NewEntity(string name) => new() { Name = name };
    protected override string NameOf(TestModel entity) => entity.Name;
    protected override void Rename(TestModel entity, string name) => entity.Name = name;
    public void Dispose() => _dirs.Dispose();
}

/// <summary>The async twin, on <see cref="AsyncJsonStore{T}"/>.</summary>
public sealed class AsyncJsonUpdateMissContractTests : AsyncStoreContract<TestModel>, IDisposable
{
    protected override bool DuplicateCreateThrowsStoreException => false; // TASK-625
    private readonly TempDirs _dirs = new();

    protected override IAsyncBulkStore<TestModel> NewStore()
    {
        var store = new AsyncJsonStore<TestModel>();
        store.SetSettings(_dirs.NewSettings("birko-json-555-"));
        return store;
    }

    protected override TestModel NewEntity(string name) => new() { Name = name };
    protected override string NameOf(TestModel entity) => entity.Name;
    protected override void Rename(TestModel entity, string name) => entity.Name = name;
    public void Dispose() => _dirs.Dispose();
}
