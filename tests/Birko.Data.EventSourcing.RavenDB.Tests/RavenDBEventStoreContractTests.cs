using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.RavenDB;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.EventSourcing;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.ServerWide.Operations;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.EventSourcing.RavenDB.Tests;

/// <summary>
/// TASK-995 — the shared event store contract on <see cref="RavenDBEventStore"/>, live, obtained as an application does:
/// <c>factory.GetEventStore(o => o.AddRavenDB())</c>. A new factory per writer, as a second process has.
/// </summary>
/// <remarks>Gated on <c>BIRKO_RAVEN_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</remarks>
public sealed class RavenDBEventStoreContractTests : EventStoreContract, IDisposable
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    private readonly string _database = "birko-task995-" + Guid.NewGuid().ToString("N");

    public RavenDBEventStoreContractTests(ITestOutputHelper output) => _output = output;

    protected override bool Ready()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL (e.g. http://localhost:8080); "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private IStoreFactory NewFactory()
        => Birko.Data.Stores.StoreFactory.Create("RavenDB", $"Url={Url};Database={_database}", p => p.AddRavenDB());

    protected override Task<IAsyncEventStore> NewStoreAsync() => Task.FromResult(NewFactory().GetEventStore(o => o.AddRavenDB()));

    [Fact]
    public void A_RavenDB_factory_answers_the_RavenDB_event_store()
    {
        if (!Ready()) return;
        NewFactory().GetEventStore(o => o.AddRavenDB()).Should().BeOfType<RavenDBEventStore>();
        NewFactory().GetEventStore().Should().BeOfType<AsyncStoreEventStore>("without the override the default is answered");
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try
        {
            using var server = new DocumentStore { Urls = new[] { Url! } }.Initialize();
            server.Maintenance.Server.Send(new DeleteDatabasesOperation(_database, hardDelete: true));
        }
        catch { }
    }
}
