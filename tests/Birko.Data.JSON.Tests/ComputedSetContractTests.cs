using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Birko.Data.JSON.Stores;

namespace Birko.Data.JSON.Tests;

/// <summary>A row for TASK-826's computed-set contract.</summary>
public class ComputedSetRow : AbstractModel
{
    public string? Code { get; set; }
    public decimal Gross { get; set; }
    public decimal Net { get; set; }
}

/// <summary>TASK-826's computed-set contract on <see cref="JsonStore{T}"/>: the read-modify-save fallback.</summary>
public sealed class JsonComputedSetContractTests : ComputedSetContract<ComputedSetRow>, IDisposable
{
    private readonly TempDirs _dirs = new();

    protected override IBulkStore<ComputedSetRow> NewStore()
    {
        var store = new JsonStore<ComputedSetRow>();
        store.SetSettings(_dirs.NewSettings("birko-json-826-"));
        return store;
    }

    protected override ComputedSetRow NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetRow, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetRow, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetRow, decimal>> Net => x => x.Net;
    public void Dispose() => _dirs.Dispose();
}

/// <summary>The async twin, on <see cref="AsyncJsonStore{T}"/>.</summary>
public sealed class AsyncJsonComputedSetContractTests : AsyncComputedSetContract<ComputedSetRow>, IDisposable
{
    private readonly TempDirs _dirs = new();

    protected override IAsyncBulkStore<ComputedSetRow> NewStore()
    {
        var store = new AsyncJsonStore<ComputedSetRow>();
        store.SetSettings(_dirs.NewSettings("birko-json-826-"));
        return store;
    }

    protected override ComputedSetRow NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetRow, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetRow, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetRow, decimal>> Net => x => x.Net;
    public void Dispose() => _dirs.Dispose();
}
