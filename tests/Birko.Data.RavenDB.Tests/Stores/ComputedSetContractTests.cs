using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Birko.Data.RavenDB.Stores;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>A row for TASK-826's computed-set contract.</summary>
public class ComputedSetDoc : AbstractModel
{
    public string? Code { get; set; }
    public decimal Gross { get; set; }
    public decimal Net { get; set; }
}

/// <summary>TASK-826's computed-set contract on <see cref="RavenDBStore{T}"/>, live: the read-modify-save fallback (a
/// server-side patch is TASK-827).</summary>
public sealed class RavenComputedSetContractTests : ComputedSetContract<ComputedSetDoc>, IDisposable
{
    private readonly RavenLive _live;
    public RavenComputedSetContractTests(ITestOutputHelper output) => _live = new RavenLive(output);
    protected override bool Ready() => _live.Ready();
    protected override IBulkStore<ComputedSetDoc> NewStore() => _live.Track((url, db) => new RavenDBStore<ComputedSetDoc>(url, db));
    protected override void Settle(IBulkStore<ComputedSetDoc> store) => _live.WaitForIndexes();
    protected override ComputedSetDoc NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetDoc, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetDoc, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetDoc, decimal>> Net => x => x.Net;
    public void Dispose() => _live.Dispose();
}

/// <summary>The async twin, on <see cref="AsyncRavenDBStore{T}"/>.</summary>
public sealed class AsyncRavenComputedSetContractTests : AsyncComputedSetContract<ComputedSetDoc>, IDisposable
{
    private readonly RavenLive _live;
    public AsyncRavenComputedSetContractTests(ITestOutputHelper output) => _live = new RavenLive(output);
    protected override bool Ready() => _live.Ready();
    protected override IAsyncBulkStore<ComputedSetDoc> NewStore() => _live.Track((url, db) => new AsyncRavenDBStore<ComputedSetDoc>(url, db));
    protected override void Settle(IAsyncBulkStore<ComputedSetDoc> store) => _live.WaitForIndexes();
    protected override ComputedSetDoc NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetDoc, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetDoc, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetDoc, decimal>> Net => x => x.Net;
    public void Dispose() => _live.Dispose();
}
