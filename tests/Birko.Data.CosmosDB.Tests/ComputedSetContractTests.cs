using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Birko.Data.CosmosDB.Stores;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>A row for TASK-826's computed-set contract.</summary>
public class ComputedSetDoc : AbstractModel
{
    public string? Code { get; set; }
    public decimal Gross { get; set; }
    public decimal Net { get; set; }
}

/// <summary>TASK-826's computed-set contract on <see cref="CosmosDBStore{T}"/>, live: the read-modify-save fallback.</summary>
public sealed class CosmosComputedSetContractTests : ComputedSetContract<ComputedSetDoc>, IDisposable
{
    private readonly CosmosLive _live;
    public CosmosComputedSetContractTests(ITestOutputHelper output) => _live = new CosmosLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IBulkStore<ComputedSetDoc> NewStore()
    {
        var (conn, db, settings) = _live.Next();
        return new CosmosDBStore<ComputedSetDoc>(conn, db, null, settings);
    }

    protected override ComputedSetDoc NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetDoc, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetDoc, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetDoc, decimal>> Net => x => x.Net;
    public void Dispose() => _live.Dispose();
}

/// <summary>The async twin, on <see cref="AsyncCosmosDBStore{T}"/>.</summary>
public sealed class AsyncCosmosComputedSetContractTests : AsyncComputedSetContract<ComputedSetDoc>, IDisposable
{
    private readonly CosmosLive _live;
    public AsyncCosmosComputedSetContractTests(ITestOutputHelper output) => _live = new CosmosLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<ComputedSetDoc> NewStore()
    {
        var (conn, db, settings) = _live.Next();
        return new AsyncCosmosDBStore<ComputedSetDoc>(conn, db, null, settings);
    }

    protected override ComputedSetDoc NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetDoc, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetDoc, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetDoc, decimal>> Net => x => x.Net;
    public void Dispose() => _live.Dispose();
}
