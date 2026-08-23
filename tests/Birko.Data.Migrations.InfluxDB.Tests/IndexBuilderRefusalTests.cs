using System;
using Birko.Data.Patterns.Schema;
using FluentAssertions;
using Xunit;
using Birko.Data.Migrations.InfluxDB.Context;
using InfluxDB.Client;

namespace Birko.Data.Migrations.InfluxDB.Tests;

/// <summary>
/// TASK-274 — this backend's migration index builder refuses what it cannot honour instead of accepting it
/// and doing nothing.
/// </summary>
/// <remarks>
/// <para>
/// InfluxDB has no custom indexes at all — the time-series structure is implicit.
/// </para>
/// <para>
/// Every method on <c>IIndexBuilder</c> returns <c>this</c>, so a builder that cannot express something has a
/// silent option at every step — and this one took it. Refusing was affordable because nothing called it:
/// measured 0 uses of <c>.Sparse()</c> and <c>.WithProperty(</c> across the framework, its tests and all 16
/// consumer repos. No server is needed: the refusals happen before any request.
/// </para>
/// </remarks>
public class IndexBuilderRefusalTests
{
    private static IIndexBuilder Builder()
        => new InfluxDBSchemaBuilder(new InfluxDBClient("http://localhost:8086", "token"), "org")
            .CreateIndex("Rows", "ix_probe");

    /// <summary>
    /// InfluxDB has no custom indexes at all, so the refusal is at the terminal: the chain's other methods
    /// stay no-ops deliberately, and <c>Build()</c> reports the whole declaration once rather than failing on
    /// whichever knob the author happened to touch first.
    /// </summary>
    [Fact]
    public void Build_refuses_because_influx_has_no_custom_indexes()
    {
        Action act = () => Builder().WithField("Code").Unique().Sparse().Build();

        act.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("no custom indexes");
    }
}
