using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Birko.Data.InMemory.Stores;

namespace Birko.Data.InMemory.Tests;

/// <summary>A row for TASK-826's computed-set contract.</summary>
public class ComputedSetRow : AbstractModel
{
    public string? Code { get; set; }
    public decimal Gross { get; set; }
    public decimal Net { get; set; }
}

/// <summary>TASK-826's computed-set contract on <see cref="InMemoryStore{T}"/>: the read-modify-save fallback.</summary>
public class InMemoryComputedSetContractTests : ComputedSetContract<ComputedSetRow>
{
    protected override IBulkStore<ComputedSetRow> NewStore() => new InMemoryStore<ComputedSetRow>();
    protected override ComputedSetRow NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetRow, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetRow, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetRow, decimal>> Net => x => x.Net;
}

/// <summary>The async twin, on <see cref="AsyncInMemoryStore{T}"/>.</summary>
public class AsyncInMemoryComputedSetContractTests : AsyncComputedSetContract<ComputedSetRow>
{
    protected override IAsyncBulkStore<ComputedSetRow> NewStore() => new AsyncInMemoryStore<ComputedSetRow>();
    protected override ComputedSetRow NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetRow, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetRow, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetRow, decimal>> Net => x => x.Net;
}
