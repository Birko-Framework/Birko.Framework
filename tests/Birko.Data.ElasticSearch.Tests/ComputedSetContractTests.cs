using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Birko.Data.ElasticSearch.Stores;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>A row for TASK-826's computed-set contract.</summary>
public class ComputedSetDoc : AbstractModel
{
    public string? Code { get; set; }
    public decimal Gross { get; set; }
    public decimal Net { get; set; }
}

/// <summary>TASK-826's computed-set contract on <see cref="ElasticSearchStore{T}"/>, live: a BigDecimal painless script.</summary>
public sealed class ElasticComputedSetContractTests : ComputedSetContract<ComputedSetDoc>, IDisposable
{
    private readonly ElasticLive _live;
    public ElasticComputedSetContractTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IBulkStore<ComputedSetDoc> NewStore()
    {
        var store = new ElasticSearchStore<ComputedSetDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override void Settle(IBulkStore<ComputedSetDoc> store) => ElasticLive.Refresh();
    protected override ComputedSetDoc NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetDoc, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetDoc, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetDoc, decimal>> Net => x => x.Net;
    public void Dispose() => _live.Dispose();
}

/// <summary>The async twin, on <see cref="AsyncElasticSearchStore{T}"/>.</summary>
public sealed class AsyncElasticComputedSetContractTests : AsyncComputedSetContract<ComputedSetDoc>, IDisposable
{
    private readonly ElasticLive _live;
    public AsyncElasticComputedSetContractTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<ComputedSetDoc> NewStore()
    {
        var store = new AsyncElasticSearchStore<ComputedSetDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override void Settle(IAsyncBulkStore<ComputedSetDoc> store) => ElasticLive.Refresh();
    protected override ComputedSetDoc NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetDoc, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetDoc, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetDoc, decimal>> Net => x => x.Net;
    public void Dispose() => _live.Dispose();
}
