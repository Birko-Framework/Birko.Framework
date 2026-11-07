using System;
using System.Linq.Expressions;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>A document for TASK-826's computed-set contract; the decimals are Decimal128, the driver's default.</summary>
public class ComputedSetDoc : Birko.Data.MongoDB.Models.MongoDBModel
{
    public string? Code { get; set; }
    public decimal Gross { get; set; }
    public decimal Net { get; set; }
}

/// <summary>TASK-826's computed-set contract on <see cref="MongoDBStore{T}"/>, live: one pipeline update.</summary>
public sealed class MongoComputedSetContractTests : ComputedSetContract<ComputedSetDoc>, IDisposable
{
    private readonly MongoLive _live;
    public MongoComputedSetContractTests(ITestOutputHelper output) => _live = new MongoLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IBulkStore<ComputedSetDoc> NewStore()
    {
        var store = new MongoDBStore<ComputedSetDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override ComputedSetDoc NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetDoc, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetDoc, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetDoc, decimal>> Net => x => x.Net;
    public void Dispose() => _live.Dispose();
}

/// <summary>The async twin, on <see cref="AsyncMongoDBStore{T}"/>.</summary>
public sealed class AsyncMongoComputedSetContractTests : AsyncComputedSetContract<ComputedSetDoc>, IDisposable
{
    private readonly MongoLive _live;
    public AsyncMongoComputedSetContractTests(ITestOutputHelper output) => _live = new MongoLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<ComputedSetDoc> NewStore()
    {
        var store = new AsyncMongoDBStore<ComputedSetDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override ComputedSetDoc NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetDoc, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetDoc, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetDoc, decimal>> Net => x => x.Net;
    public void Dispose() => _live.Dispose();
}
