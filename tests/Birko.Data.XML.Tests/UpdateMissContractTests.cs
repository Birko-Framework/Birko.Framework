using System;
using System.Collections.Generic;
using System.IO;
using Birko.Configuration;
using Birko.Data.Stores;
using Birko.Data.XML.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.XML.Tests;

/// <summary>Creates a store per test in its own temp directory and removes them all afterwards.</summary>
internal sealed class TempDirs : IDisposable
{
    private readonly List<string> _dirs = new();

    public Settings NewSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "birko-xml-555-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        // BatchSettings is a Settings, so one instance serves all three layouts (the batch store requires it).
        return new BatchSettings { Location = dir, Name = "contract.xml", BatchSize = 2 };
    }

    public void Dispose()
    {
        foreach (var d in _dirs)
        {
            try { Directory.Delete(d, recursive: true); } catch { }
        }
    }
}

/// <summary>TASK-555's update-miss contract on the three sync XML layouts (one file, file per entity, batches).</summary>
public abstract class XmlUpdateMissContractBase<TStore> : StoreContract<TestModel>, IDisposable
    where TStore : AbstractXmlStore<TestModel>, ISettingsStore<Settings>, new()
{
    protected override bool DuplicateCreateThrowsStoreException => false; // TASK-625
    private readonly TempDirs _dirs = new();

    protected override IBulkStore<TestModel> NewStore()
    {
        var store = new TStore();
        store.SetSettings(_dirs.NewSettings());
        return store;
    }

    protected override TestModel NewEntity(string name) => new() { Name = name };
    protected override string NameOf(TestModel entity) => entity.Name;
    protected override void Rename(TestModel entity, string name) => entity.Name = name;
    public void Dispose() => _dirs.Dispose();
}

public sealed class XmlStoreUpdateMissContractTests : XmlUpdateMissContractBase<XmlStore<TestModel>> { }
public sealed class XmlSeparateStoreUpdateMissContractTests : XmlUpdateMissContractBase<XmlSeparateStore<TestModel>> { }
public sealed class XmlBatchStoreUpdateMissContractTests : XmlUpdateMissContractBase<XmlBatchStore<TestModel>> { }

/// <summary>The async twins.</summary>
public abstract class AsyncXmlUpdateMissContractBase<TStore> : AsyncStoreContract<TestModel>, IDisposable
    where TStore : AbstractAsyncXmlStore<TestModel>, ISettingsStore<Settings>, new()
{
    protected override bool DuplicateCreateThrowsStoreException => false; // TASK-625
    private readonly TempDirs _dirs = new();

    protected override IAsyncBulkStore<TestModel> NewStore()
    {
        var store = new TStore();
        store.SetSettings(_dirs.NewSettings());
        return store;
    }

    protected override TestModel NewEntity(string name) => new() { Name = name };
    protected override string NameOf(TestModel entity) => entity.Name;
    protected override void Rename(TestModel entity, string name) => entity.Name = name;
    public void Dispose() => _dirs.Dispose();
}

public sealed class AsyncXmlStoreUpdateMissContractTests : AsyncXmlUpdateMissContractBase<AsyncXmlStore<TestModel>> { }
public sealed class AsyncXmlSeparateStoreUpdateMissContractTests : AsyncXmlUpdateMissContractBase<AsyncXmlSeparateStore<TestModel>> { }
public sealed class AsyncXmlBatchStoreUpdateMissContractTests : AsyncXmlUpdateMissContractBase<AsyncXmlBatchStore<TestModel>> { }
