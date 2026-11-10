using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-895's string-equality leftovers contract on <see cref="PostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLStringEqualityLeftoversContractTests : StringEqualityLeftoversContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLStringEqualityLeftoversContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override int MaxParameters => 65535;

    protected override (IBulkStore<StringLeftoverRow> Store, AbstractConnector Connector) NewStore()
    {
        var store = new PostgreSQLStore<StringLeftoverRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return (store, store.Connector);
    }
}
