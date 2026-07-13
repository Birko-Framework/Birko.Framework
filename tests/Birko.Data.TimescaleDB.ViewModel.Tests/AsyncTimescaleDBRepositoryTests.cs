using System;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using Birko.Data.Stores;
using Birko.Data.SQL.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace Birko.Data.TimescaleDB.ViewModel.Tests;

/// <summary>
/// CR-M178: the TimescaleDB.ViewModel repositories had no .Tests sibling. These cover the constructor
/// store-type guard, SetSettings routing (Connector becomes available), and the "Connector not
/// initialized" lifecycle guards — all offline (no live DB).
/// </summary>
public class AsyncTimescaleDBRepositoryTests
{
    public sealed class Metric : Birko.Data.Models.AbstractModel
    {
        public double Value { get; set; }
    }

    public sealed class MetricVm : Birko.Data.Models.ILoadable<Metric>
    {
        public void LoadFrom(Metric data) { }
    }

    private sealed class MetricRepository : AsyncTimescaleDBRepository<MetricVm, Metric>
    {
        public MetricRepository() : base() { }
        public MetricRepository(IAsyncStore<Metric>? store) : base(store) { }
        public IAsyncStore<Metric>? StoreForTest => Store;
        protected override void MapToModel(MetricVm source, Metric target) { }
    }

    [Fact]
    public void Constructor_Default_UsesTimescaleStore_ConnectorNullUntilSetSettings()
    {
        var repo = new MetricRepository();

        repo.StoreForTest.Should().BeOfType<AsyncTimescaleDBStore<Metric>>();
        repo.Connector.Should().BeNull("no settings applied yet");
    }

    [Fact]
    public void Constructor_ForeignStore_ThrowsArgumentException()
    {
        var foreign = new Mock<IAsyncStore<Metric>>().Object;

        var act = () => new MetricRepository(foreign);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetSettings_TimescaleSettings_MakesConnectorAvailable()
    {
        var repo = new MetricRepository();

        repo.SetSettings(new TimescaleDBSettings("host", "db", "u", "p", 5432));

        repo.Connector.Should().NotBeNull();
        repo.Connector.Should().BeOfType<TimescaleDBConnector>();
    }

    [Fact]
    public void SetSettings_RemoteSettings_MakesConnectorAvailable()
    {
        var repo = new MetricRepository();

        repo.SetSettings(new Birko.Configuration.RemoteSettings("host", "db", "u", "p", 5432));

        repo.Connector.Should().NotBeNull();
    }

    [Fact]
    public async Task LifecycleMethods_WithoutSetSettings_ThrowInvalidOperation()
    {
        var repo = new MetricRepository();

        await repo.Invoking(r => r.InitAsync()).Should().ThrowAsync<InvalidOperationException>();
        await repo.Invoking(r => r.DropAsync()).Should().ThrowAsync<InvalidOperationException>();
        await repo.Invoking(r => r.CreateSchemaAsync()).Should().ThrowAsync<InvalidOperationException>();
        await repo.Invoking(r => r.CreateHypertableAsync("ts")).Should().ThrowAsync<InvalidOperationException>();
    }
}
