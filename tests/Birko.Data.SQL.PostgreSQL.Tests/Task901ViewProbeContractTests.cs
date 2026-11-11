using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-901's view-probe contract on <see cref="PostgreSQLConnector"/> without <c>Birko.Data.SQL.PostgreSQL.View</c>, live.</summary>
public sealed class PostgreSQLViewProbeContractTests : ViewProbeContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLViewProbeContractTests(ITestOutputHelper output) : base(output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();
    protected override string ProviderName => "PostgreSQL";
    protected override AbstractConnector NewConnector() => new PostgreSQLConnector(_live.Settings());

    protected override AbstractConnector UnreachableConnector()
    {
        var settings = _live.Settings();
        settings.Password = "not-the-password-901";
        return new PostgreSQLConnector(settings);
    }
}
