using System;
using Birko.Data.CosmosDB.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-223 — <c>GetCosmosClientOptions()</c> set the timeout, bulk flag and serializer but never
/// <c>ConnectionMode</c>, so the SDK default (<b>Direct</b>, over TCP) always applied and there was no
/// way to ask for anything else: a Cosmos connection string cannot carry a connection mode, and the
/// store's connection-string constructor took no options.
///
/// <para>
/// Gateway routes everything over HTTPS on the account endpoint instead of opening TCP connections to
/// per-partition replicas. It is the only mode that works where the Direct port range is blocked — behind
/// a corporate proxy or firewall, and against the Azure Cosmos DB emulator, which serves Gateway only.
/// Without it neither was reachable at all.
/// </para>
///
/// Non-gated: this is client-options construction, not a round trip.
/// </summary>
public class CosmosConnectionModeTests
{
    [Fact]
    public void The_default_is_unchanged()
    {
        // The whole point of defaulting to Direct is that no existing consumer changes behaviour. If this
        // ever flips, every deployment silently moves onto a slower transport.
        new Settings().ConnectionMode.Should().Be(ConnectionMode.Direct);
        new Settings().GetCosmosClientOptions().ConnectionMode.Should().Be(ConnectionMode.Direct);
    }

    [Theory]
    [InlineData(ConnectionMode.Gateway)]
    [InlineData(ConnectionMode.Direct)]
    public void The_setting_reaches_the_client_options(ConnectionMode mode)
    {
        new Settings { ConnectionMode = mode }
            .GetCosmosClientOptions().ConnectionMode.Should().Be(mode);
    }

    [Fact]
    public void LoadFrom_carries_the_connection_mode()
    {
        // LoadFrom copies the other three settings; omitting this one would lose the mode silently
        // wherever settings are cloned or loaded from configuration — a wrong transport, no error.
        var source = new Settings("https://acct", "db") { ConnectionMode = ConnectionMode.Gateway };

        var target = new Settings();
        target.LoadFrom(source);

        target.ConnectionMode.Should().Be(ConnectionMode.Gateway);
    }

    [Fact]
    public void The_connection_string_constructor_accepts_settings()
    {
        // The constructor the live suite uses. Without this parameter there is no way in at all, because
        // a Cosmos connection string cannot express a connection mode.
        var act = () => new AsyncCosmosDBStore<TestDoc>(
            "AccountEndpoint=https://localhost:8081/;AccountKey="
            + "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;",
            "db", null, new Settings { ConnectionMode = ConnectionMode.Gateway });

        act.Should().NotThrow("constructing a client opens no connection");
    }

    public class TestDoc : Birko.Data.Models.AbstractModel { public string? Name { get; set; } }
}
