using Birko.Data.TimescaleDB.Stores;
using Birko.Data.Models;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.TimescaleDB.Tests;

public class TestModel : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class TimescaleDBStoreTests
{
    [Fact]
    public void Constructor_Default_ShouldNotThrow()
    {
        var store = new TimescaleDBStore<TestModel>();
        store.Should().NotBeNull();
    }

    [Fact]
    public void Settings_Default_ShouldHaveDefaults()
    {
        var settings = new TimescaleDBSettings();
        settings.TimeColumn.Should().Be("timestamp");
        settings.ChunkTimeInterval.Should().Be("7 days");
    }
}
