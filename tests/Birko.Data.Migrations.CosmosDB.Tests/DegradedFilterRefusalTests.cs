using System;
using System.Collections.Generic;
using Birko.Data.Exceptions;
using Birko.Data.Migrations.CosmosDB.Context;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Birko.Data.Migrations.CosmosDB.Tests;

/// <summary>
/// <b>SH-H032 (TASK-314).</b> <c>{"status":{}}</c> takes <see cref="CosmosDBDataMigrator.ParseFilterToSql"/>'s
/// object branch and its operator loop adds nothing, so the clause came back empty and the caller fell
/// through to <c>SELECT c.id FROM c</c> — every document in the container, then deleted or patched one by
/// one.
///
/// <para>
/// <b>No live account is needed, and that is by construction rather than by luck.</b> The filter is parsed
/// and guarded before <c>GetPartitionKeyProperty</c>, whose <c>ReadContainerAsync</c> is the first network
/// call — so the refusal is reachable offline. Same shape as this project's existing helper tests and
/// § TASK-309's rule: try rendering a sink before accepting it is untestable.
/// </para>
/// </summary>
public class DegradedFilterRefusalTests
{
    /// <summary>
    /// An account that cannot be reached: a refusal test that ever touched it would fail loudly instead of
    /// passing for the wrong reason.
    /// </summary>
    private static CosmosDBDataMigrator Migrator()
    {
        var client = new CosmosClient(
            "AccountEndpoint=https://localhost:1/;AccountKey=" +
            Convert.ToBase64String(new byte[64]) + ";",
            new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        return new CosmosDBDataMigrator(client.GetDatabase("unused"));
    }

    [Fact]
    public void A_delete_whose_filter_constrains_nothing_is_refused_before_any_request()
    {
        Action act = () => Migrator().DeleteDocuments("Widgets", "{\"status\":{}}");

        var ex = act.Should().Throw<WholeTableWriteException>().Which;
        ex.Operation.Should().Be("delete");
        ex.TableName.Should().Be("Widgets");
        ex.Message.Should().Contain("every document in the container");
    }

    [Fact]
    public void An_update_whose_filter_constrains_nothing_is_refused_before_any_request()
    {
        Action act = () => Migrator().UpdateDocuments(
            "Widgets", "{\"status\":{}}", new Dictionary<string, object> { ["Name"] = "x" });

        act.Should().Throw<WholeTableWriteException>()
            .Which.Operation.Should().Be("update");
    }

    /// <summary>
    /// Guard the whole verb family (§ TASK-215): a count must not quietly answer for the whole container
    /// while a delete built from the identical filter is refused.
    /// </summary>
    [Fact]
    public void A_count_whose_filter_constrains_nothing_is_refused_before_any_request()
    {
        Action act = () => Migrator().CountDocuments("Widgets", "{\"status\":{}}");

        act.Should().Throw<WholeTableWriteException>()
            .Which.Operation.Should().Be("count");
    }

    // ---------------------------------------------------------------- contract pins
    // The guard must not be too broad: an explicit match-all and an ordinary filter both have to get past
    // it. Without these, "refuse every filter" would pass every test above and look like a valid fix.

    /// <summary>
    /// Asserts the guard let <paramref name="filterJson"/> through, without waiting on a doomed request.
    ///
    /// <para>
    /// A refusal is raised <b>before any I/O</b> — that is the whole point of parsing and guarding ahead of
    /// <c>GetPartitionKeyProperty</c> — so it is effectively instantaneous. Anything that is still running
    /// after the grace period has therefore already passed the guard and is sitting in the SDK's retry
    /// policy against the unreachable endpoint, which takes ~13s and tells us nothing further. Asserting
    /// the timing property directly is both faster and a more exact statement than waiting for the network
    /// to fail and then inspecting the exception type.
    /// </para>
    /// </summary>
    private static void ShouldNotBeRefused(string? filterJson)
    {
        var task = System.Threading.Tasks.Task.Run(
            () => Migrator().DeleteDocuments("Widgets", filterJson!));

        if (!task.Wait(TimeSpan.FromSeconds(2)))
        {
            // Still going, so it is past the guard and out on the wire. That is the pass condition.
            // The task is left to die with the test process; it holds no resource of ours.
            return;
        }

        task.Exception?.Flatten().InnerException
            .Should().NotBeOfType<WholeTableWriteException>(
                "an explicit match-all and an ordinary filter must both get past the SH-H032 guard");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    public void The_explicit_match_all_door_is_not_refused(string? filterJson)
        => ShouldNotBeRefused(filterJson);

    [Fact]
    public void An_ordinary_filter_is_not_refused()
        => ShouldNotBeRefused("{\"status\":\"archived\"}");
}
