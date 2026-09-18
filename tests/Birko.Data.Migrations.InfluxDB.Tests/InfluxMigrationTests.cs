using System;
using Birko.Data.Migrations.InfluxDB;
using Birko.Data.Migrations.InfluxDB.Context;
using FluentAssertions;
using InfluxDB.Client;
using Xunit;

namespace Birko.Data.Migrations.InfluxDB.Tests;

/// <summary>
/// CR-H061: the InfluxDB migration backend had no test project. The record/copy/bulk operations
/// require a live InfluxDB (env-gated elsewhere), but the constructor guards and the pure flux
/// predicate helper are verifiable offline. (The CR-H060 fix replaced the leaked batching
/// GetWriteApi() with the synchronous GetWriteApiAsync().)
/// </summary>
public class InfluxMigrationTests
{
    // InfluxDBClient construction is lazy — no network until a request is made.
    private static InfluxDBClient NewClient() => new InfluxDBClient("http://localhost:8086", "token");

    [Fact]
    public void Store_NullClient_Throws()
    {
        var act = () => new InfluxMigrationStore(null!, "org");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Runner_NullClient_Throws()
    {
        var act = () => new InfluxMigrationRunner(null!, "org");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DataMigrator_NullClient_Throws()
    {
        var act = () => new InfluxDBDataMigrator(null!, "org");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructs_WithLazyClient_WithoutConnecting()
    {
        using var client = NewClient();

        var act = () =>
        {
            _ = new InfluxMigrationStore(client, "org");
            _ = new InfluxMigrationRunner(client, "org");
            _ = new InfluxDBDataMigrator(client, "org");
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void ConvertFilterToFluxPredicate_IsPassthrough()
    {
        InfluxDBDataMigrator.ConvertFilterToFluxPredicate("r.host == \"a\"").Should().Be("r.host == \"a\"");
    }
}
