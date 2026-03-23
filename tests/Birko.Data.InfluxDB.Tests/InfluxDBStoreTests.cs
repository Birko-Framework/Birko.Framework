using Birko.Data.InfluxDB.Stores;
using Birko.Data.Models;
using FluentAssertions;
using System;
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
}
