using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Birko.Data.SQL.TimescaleDB.Stores;
using Xunit.Abstractions;

namespace Birko.Data.TimescaleDB.Tests;

/// <summary>Rows for TASK-826's computed-set contract; sync and async run in parallel, so each has its own table.</summary>
[Table("ComputedSetSyncRows")]
public class ComputedSetSyncRow : AbstractModel
{
    public string? Code { get; set; }
    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal Gross { get; set; }
    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal Net { get; set; }
    public DateTime Ts { get; set; } = DateTime.UtcNow;
}

[Table("ComputedSetAsyncRows")]
public class ComputedSetAsyncRow : AbstractModel
{
    public string? Code { get; set; }
    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal Gross { get; set; }
    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal Net { get; set; }
    public DateTime Ts { get; set; } = DateTime.UtcNow;
}

/// <summary>TASK-826's computed-set contract on <see cref="TimescaleDBStore{T}"/>, live: the formula runs in the server's SQL.</summary>
public sealed class TimescaleDBComputedSetContractTests : ComputedSetContract<ComputedSetSyncRow>
{
    private readonly TimescaleDBLive _live;
    public TimescaleDBComputedSetContractTests(ITestOutputHelper output) => _live = new TimescaleDBLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IBulkStore<ComputedSetSyncRow> NewStore()
    {
        var store = new TimescaleDBStore<ComputedSetSyncRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }

    protected override ComputedSetSyncRow NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetSyncRow, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetSyncRow, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetSyncRow, decimal>> Net => x => x.Net;
}

/// <summary>The async twin, on <see cref="AsyncTimescaleDBStore{T}"/>.</summary>
public sealed class AsyncTimescaleDBComputedSetContractTests : AsyncComputedSetContract<ComputedSetAsyncRow>
{
    private readonly TimescaleDBLive _live;
    public AsyncTimescaleDBComputedSetContractTests(ITestOutputHelper output) => _live = new TimescaleDBLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<ComputedSetAsyncRow> NewStore()
    {
        var store = new AsyncTimescaleDBStore<ComputedSetAsyncRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override ComputedSetAsyncRow NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetAsyncRow, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetAsyncRow, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetAsyncRow, decimal>> Net => x => x.Net;
}
