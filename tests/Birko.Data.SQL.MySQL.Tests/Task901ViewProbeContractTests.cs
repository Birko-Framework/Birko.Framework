using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-901's view-probe contract on <see cref="MySQLConnector"/> without <c>Birko.Data.SQL.MySQL.View</c>, live.</summary>
public sealed class MySQLViewProbeContractTests : ViewProbeContract
{
    private readonly MySQLLive _live;
    public MySQLViewProbeContractTests(ITestOutputHelper output) : base(output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();
    protected override string ProviderName => "MySQL";
    protected override AbstractConnector NewConnector() => new MySQLConnector(_live.Settings());

    protected override AbstractConnector UnreachableConnector()
    {
        var settings = _live.Settings();
        settings.Password = "not-the-password-901";
        return new MySQLConnector(settings);
    }
}
