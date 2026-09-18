using System;
using Birko.Data.Migrations.CosmosDB.Context;
using Birko.Data.Patterns.Schema;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Birko.Data.Migrations.CosmosDB.Tests;

/// <summary>
/// TASK-274 — this backend's migration index builder refuses what it cannot honour instead of accepting it
/// and doing nothing.
/// </summary>
/// <remarks>
/// <para>
/// Cosmos DB has no individual index objects — indexing is a container-level policy — so an index declared
/// through this builder can never be honoured. It nevertheless accumulated fields and a <c>Unique()</c> flag,
/// held a live <c>Database</c>, and inherited <c>IIndexBuilder.Build()</c>'s no-op default: a migration read
/// as though it had declared an index and nothing was ever created. TASK-246's lost-flag defect, total rather
/// than partial.
/// </para>
/// <para>
/// No emulator is needed: <c>CosmosClient</c> construction is lazy and every refusal here happens before any
/// request would be sent.
/// </para>
/// </remarks>
public class IndexBuilderRefusalTests
{
    private const string LocalEmulatorKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private static IIndexBuilder Builder()
    {
        var client = new CosmosClient("https://localhost:8081/", LocalEmulatorKey);
        return new CosmosDBSchemaBuilder(client.GetDatabase("probe")).CreateIndex("Rows", "ix_probe");
    }

    [Fact]
    public void Sparse_is_refused()
    {
        Action act = () => Builder().WithField("Code").Sparse();

        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("sparse index");
    }

    [Fact]
    public void WithProperty_is_refused()
    {
        Action act = () => Builder().WithField("Code").WithProperty("k", 1);

        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("index property 'k'");
    }

    [Fact]
    public void Build_refuses_rather_than_creating_nothing()
    {
        Action act = () => Builder().WithField("Code").Unique().Build();

        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("does not create indexes");
    }
}
