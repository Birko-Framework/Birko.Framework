using System;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

public class NullOrderingDoc : AbstractModel
{
    public string? Code { get; set; }
    public int A { get; set; }
    public int? Score { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-865's null-ordering scope contract (<c>tests/Shared/StoreContract</c>) on the RavenDB stores, live.</summary>
public sealed class RavenNullOrderingScopeContractTests : NullOrderingScopeContract<NullOrderingDoc>, IDisposable
{
    private readonly RavenLive _live;
    public RavenNullOrderingScopeContractTests(ITestOutputHelper output) => _live = new RavenLive(output);
    protected override bool Ready() => _live.Ready();

    /// <summary>Measured 2026-11-08 (TASK-865): RQL's <c>Score &gt; null</c> matches every non-null Score, where C# matches
    /// nothing, so these filters read and write other rows than C#, silently. Not fixed here (a read translation).</summary>
    protected override string? KnownTranslationGap(string shape) => shape switch
    {
        "Score > nul || A > 4" or "!(Score > nul || A > 3)" or "!(Score > nul) && A > 4"
            => "RQL ranks null below every value, so an ordering against null matches the non-null rows",
        _ => null,
    };
    protected override IAsyncBulkStore<NullOrderingDoc> NewAsyncStore() => _live.Track((url, db) => new AsyncRavenDBStore<NullOrderingDoc>(url, db));
    protected override IBulkStore<NullOrderingDoc> NewSyncStore() => _live.Track((url, db) => new RavenDBStore<NullOrderingDoc>(url, db));
    public void Dispose() => _live.Dispose();
}
