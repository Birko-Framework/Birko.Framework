using System;
using System.IO;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>A row for TASK-826's computed-set contract.</summary>
[Table("ComputedSetRows")]
public class ComputedSetRow : AbstractModel
{
    public string? Code { get; set; }

    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal Gross { get; set; }

    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal Net { get; set; }
}

/// <summary>TASK-826's computed-set contract on <see cref="SQLiteStore{T}"/>: the formula runs in SQLite's exact decimal functions.</summary>
public sealed class SQLiteComputedSetContractTests : ComputedSetContract<ComputedSetRow>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-826-" + Guid.NewGuid().ToString("N"));

    public SQLiteComputedSetContractTests() => Directory.CreateDirectory(_root);

    protected override IBulkStore<ComputedSetRow> NewStore()
    {
        var store = new SQLiteStore<ComputedSetRow>();
        store.SetSettings(new SqLiteSettings(_root, $"sync-{Guid.NewGuid():N}.db"));
        return store;
    }

    protected override ComputedSetRow NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetRow, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetRow, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetRow, decimal>> Net => x => x.Net;

    public void Dispose() => TestTempDirectory.Delete(_root);
}

/// <summary>The async twin, on <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class AsyncSQLiteComputedSetContractTests : AsyncComputedSetContract<ComputedSetRow>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-826-" + Guid.NewGuid().ToString("N"));

    public AsyncSQLiteComputedSetContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<ComputedSetRow> NewStore()
    {
        var store = new AsyncSQLiteStore<ComputedSetRow>();
        store.SetSettings(new SqLiteSettings(_root, $"async-{Guid.NewGuid():N}.db"));
        return store;
    }

    protected override ComputedSetRow NewRow(string code, decimal gross, decimal net) => new() { Code = code, Gross = gross, Net = net };
    protected override Expression<Func<ComputedSetRow, string?>> Code => x => x.Code;
    protected override Expression<Func<ComputedSetRow, decimal>> Gross => x => x.Gross;
    protected override Expression<Func<ComputedSetRow, decimal>> Net => x => x.Net;

    public void Dispose() => TestTempDirectory.Delete(_root);
}
