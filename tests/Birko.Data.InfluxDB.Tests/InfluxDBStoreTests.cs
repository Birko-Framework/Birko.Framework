using Birko.Data.InfluxDB.Stores;
using Birko.Data.Models;
using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.InfluxDB.Tests;

public class TestModel : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
}

public class InfluxDBStoreTests
{
    [Fact]
    public void Constructor_Default_ShouldNotThrow()
    {
        var store = new InfluxDBStore<TestModel>();
        store.Should().NotBeNull();
    }

    [Fact]
    public void Settings_ShouldHaveCorrectDefaults()
    {
        var settings = new Settings("http://localhost:8086", "testbucket", null, null);
        settings.Location.Should().Be("http://localhost:8086");
        settings.Name.Should().Be("testbucket");
    }

    [Fact]
    public void Read_WithNoClient_ShouldReturnNull()
    {
        var store = new InfluxDBStore<TestModel>();
        var result = store.Read(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public void Count_WithNoClient_ShouldReturnZero()
    {
        var store = new InfluxDBStore<TestModel>();
        var result = store.Count();
        result.Should().Be(0);
    }

    // --- CR-H049: the store owns an IDisposable InfluxDBClient and must release it ---

    [Fact]
    public void SyncStore_IsDisposable()
    {
        typeof(InfluxDBStore<TestModel>).Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public void AsyncStore_IsDisposable()
    {
        typeof(AsyncInfluxDBStore<TestModel>).Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public void SetSettings_ThenDispose_ReleasesClient()
    {
        var store = new InfluxDBStore<TestModel>();
        store.SetSettings(new Settings("http://localhost:8086", "b", "tok", "org"));
        store.Client.Should().NotBeNull();

        store.Dispose();

        store.Client.Should().BeNull("Dispose must release the owned client");
    }

    [Fact]
    public void SetSettings_Twice_DoesNotThrow_AndReplacesClient()
    {
        var store = new InfluxDBStore<TestModel>();

        var act = () =>
        {
            store.SetSettings(new Settings("http://localhost:8086", "b1", "tok", "org"));
            store.SetSettings(new Settings("http://localhost:8087", "b2", "tok", "org")); // disposes the first
        };

        act.Should().NotThrow();
        store.Client.Should().NotBeNull();
        store.Dispose();
    }

    [Fact]
    public async Task AsyncStore_SetSettings_ThenDispose_ReleasesClient()
    {
        var store = new AsyncInfluxDBStore<TestModel>();
        store.SetSettings(new Settings("http://localhost:8086", "b", "tok", "org"));
        store.Client.Should().NotBeNull();

        store.Dispose();

        store.Client.Should().BeNull();
        await Task.CompletedTask;
    }
}
