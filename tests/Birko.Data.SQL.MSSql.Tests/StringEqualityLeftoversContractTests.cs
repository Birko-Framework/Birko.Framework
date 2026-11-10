using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-895's string-equality leftovers contract on <see cref="MSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerStringEqualityLeftoversContractTests : StringEqualityLeftoversContract
{
    private readonly SQLServerLive _live;
    public SQLServerStringEqualityLeftoversContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override int MaxParameters => 2098;

    protected override (IBulkStore<StringLeftoverRow> Store, AbstractConnector Connector) NewStore()
    {
        var store = new MSSqlStore<StringLeftoverRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return (store, store.Connector);
    }
}
