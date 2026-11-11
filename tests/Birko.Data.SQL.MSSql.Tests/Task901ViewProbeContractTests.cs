using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-901's view-probe contract on <see cref="MSSqlConnector"/> without <c>Birko.Data.SQL.MSSql.View</c>, live.</summary>
public sealed class SQLServerViewProbeContractTests : ViewProbeContract
{
    private readonly SQLServerLive _live;
    public SQLServerViewProbeContractTests(ITestOutputHelper output) : base(output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();
    protected override string ProviderName => "SQL Server";
    protected override bool CreatesViewsWithoutViewPackage => false;
    protected override bool DropsSourceTableUnderAView => true;
    protected override AbstractConnector NewConnector() => new MSSqlConnector(_live.Settings());

    protected override AbstractConnector UnreachableConnector()
    {
        var settings = _live.Settings();
        settings.Password = "not-the-password-901";
        return new MSSqlConnector(settings);
    }
}
