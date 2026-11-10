using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-895's string-equality leftovers contract on <see cref="MySQLStore{T}"/>, live.</summary>
public sealed class MySQLStringEqualityLeftoversContractTests : StringEqualityLeftoversContract
{
    private readonly MySQLLive _live;
    public MySQLStringEqualityLeftoversContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override int MaxParameters => 65535;

    protected override (IBulkStore<StringLeftoverRow> Store, AbstractConnector Connector) NewStore()
    {
        var store = new MySQLStore<StringLeftoverRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return (store, store.Connector);
    }
}
