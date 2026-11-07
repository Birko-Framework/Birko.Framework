using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Birko.Configuration;
using Birko.Data.XML.Stores;

namespace Birko.Data.XML.Tests;

/// <summary>A row for TASK-826's computed-set contract.</summary>
public class ComputedSetRow : AbstractModel
{
    public string? Code { get; set; }
    public decimal Gross { get; set; }
    public decimal Net { get; set; }
}

/// <summary>TASK-826's computed-set contract on the XML stores: the read-modify-save fallback.</summary>
public abstract class XmlComputedSetContractBase<TStore> : ComputedSetContract<ComputedSetRow>, IDisposable
    where TStore : AbstractXmlStore<ComputedSetRow>, ISettingsStore<Settings>, new()
{
    private readonly TempDirs _dirs = new();

    protected override IBulkStore<ComputedSetRow> NewStore()
    {
        var store = new TStore();
        store.SetSettings(_dirs.NewSettings());
        return store;
    }

    protected override ComputedSetRow NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetRow, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetRow, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetRow, decimal>> Net => x => x.Net;
    public void Dispose() => _dirs.Dispose();
}

public sealed class XmlStoreComputedSetContractTests : XmlComputedSetContractBase<XmlStore<ComputedSetRow>> { }
public sealed class XmlSeparateStoreComputedSetContractTests : XmlComputedSetContractBase<XmlSeparateStore<ComputedSetRow>> { }
public sealed class XmlBatchStoreComputedSetContractTests : XmlComputedSetContractBase<XmlBatchStore<ComputedSetRow>> { }

/// <summary>The async twins.</summary>
public abstract class AsyncXmlComputedSetContractBase<TStore> : AsyncComputedSetContract<ComputedSetRow>, IDisposable
    where TStore : AbstractAsyncXmlStore<ComputedSetRow>, ISettingsStore<Settings>, new()
{
    private readonly TempDirs _dirs = new();

    protected override IAsyncBulkStore<ComputedSetRow> NewStore()
    {
        var store = new TStore();
        store.SetSettings(_dirs.NewSettings());
        return store;
    }

    protected override ComputedSetRow NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetRow, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetRow, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetRow, decimal>> Net => x => x.Net;
    public void Dispose() => _dirs.Dispose();
}

public sealed class AsyncXmlStoreComputedSetContractTests : AsyncXmlComputedSetContractBase<AsyncXmlStore<ComputedSetRow>> { }
public sealed class AsyncXmlSeparateStoreComputedSetContractTests : AsyncXmlComputedSetContractBase<AsyncXmlSeparateStore<ComputedSetRow>> { }
public sealed class AsyncXmlBatchStoreComputedSetContractTests : AsyncXmlComputedSetContractBase<AsyncXmlBatchStore<ComputedSetRow>> { }
