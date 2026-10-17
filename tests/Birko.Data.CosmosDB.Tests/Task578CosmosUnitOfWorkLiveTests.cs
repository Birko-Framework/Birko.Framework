using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.UnitOfWork;
using Birko.Data.Patterns.UnitOfWork;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-578 (SH-M430), live against the Cosmos emulator: a refused batch is a <see cref="UnitOfWorkException"/> naming
/// its status, and a failed commit, refused or thrown, ends the boundary as on SQL and Elasticsearch (TASK-325).
/// Gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task578CosmosUnitOfWorkLiveTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _container;

    public Task578CosmosUnitOfWorkLiveTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            _output.WriteLine("SKIPPED: no live Cosmos. Set BIRKO_COSMOS_CONNECTION.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_COSMOS_CONNECTION is not.");
            return;
        }
        _client = new CosmosClient(conn, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task578_" + Guid.NewGuid().ToString("N"))).Database;
        _container = (await _db.CreateContainerIfNotExistsAsync("Rows", "/pk")).Container;
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private sealed record Row(string id, string pk);

    [Fact]
    public async Task A_refused_batch_is_a_UnitOfWorkException_and_ends_the_boundary()
    {
        if (_container == null) return;
        await _container.CreateItemAsync(new Row("taken", "p"), new PartitionKey("p"));
        using var uow = new CosmosDbUnitOfWork(_container, new PartitionKey("p"));
        await uow.BeginAsync();
        uow.Context!.CreateItem(new Row("taken", "p"));

        var commit = () => uow.CommitAsync();

        (await commit.Should().ThrowAsync<UnitOfWorkException>()).Which.Message.Should().Contain("Conflict");
        uow.IsActive.Should().BeFalse();
        await uow.Invoking(u => u.RollbackAsync()).Should().NotThrowAsync("catch-and-roll-back after a failed commit");
        await uow.Invoking(u => u.RollbackAsync()).Should().ThrowAsync<NoActiveTransactionException>();
    }

    [Fact]
    public async Task A_throwing_execute_ends_the_boundary_and_propagates_unchanged()
    {
        if (_container == null) return;
        using var uow = new CosmosDbUnitOfWork(_container, new PartitionKey("p"));
        await uow.BeginAsync();
        uow.Context!.CreateItem(new Row(Guid.NewGuid().ToString(), "p"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var commit = () => uow.CommitAsync(cancelled.Token);

        await commit.Should().ThrowAsync<OperationCanceledException>();
        uow.IsActive.Should().BeFalse("the batch is spent once sent, whatever the outcome");
        await uow.Invoking(u => u.RollbackAsync()).Should().NotThrowAsync();
    }
}
