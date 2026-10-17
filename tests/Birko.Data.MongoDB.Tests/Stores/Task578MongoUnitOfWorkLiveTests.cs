using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.MongoDB.UnitOfWork;
using Birko.Data.Patterns.UnitOfWork;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-578 (SH-M429), live: a MongoDB unit of work releases its client session when the transaction ends, by commit,
/// abort or a failed commit, and a failed commit ends the boundary as on SQL and Elasticsearch (TASK-325). Gated on
/// <c>BIRKO_MONGO_HOST</c> (a replica set).
/// </summary>
public class Task578MongoUnitOfWorkLiveTests
{
    private readonly ITestOutputHelper _output;
    public Task578MongoUnitOfWorkLiveTests(ITestOutputHelper output) => _output = output;

    private MongoClient? Client()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        if (!string.IsNullOrWhiteSpace(host))
            return new MongoClient($"mongodb://{host}:27017/?directConnection=true");
        const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST (a replica set); set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return null;
    }

    private static async Task Write(MongoDbUnitOfWork uow, IMongoClient client, string db, string? id = null)
        => await client.GetDatabase(db).GetCollection<BsonDocument>("Rows")
            .InsertOneAsync(uow.Context!, new BsonDocument { { "_id", id ?? Guid.NewGuid().ToString() } });

    private static bool Disposed(IClientSessionHandle session)
    {
        try { session.StartTransaction(); session.AbortTransaction(); return false; }
        catch (ObjectDisposedException) { return true; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Commit_and_rollback_release_the_session(bool commit)
    {
        var client = Client();
        if (client == null) return;
        var db = "birko_task578_" + Guid.NewGuid().ToString("N");
        await client.GetDatabase(db).CreateCollectionAsync("Rows");
        try
        {
            using var uow = new MongoDbUnitOfWork(client);
            await uow.BeginAsync();
            var session = uow.Context!;
            await Write(uow, client, db);

            if (commit) await uow.CommitAsync(); else await uow.RollbackAsync();

            uow.IsActive.Should().BeFalse();
            uow.Context.Should().BeNull("the ended transaction's session is released");
            Disposed(session).Should().BeTrue("the session was disposed, not left for the next BeginAsync to overwrite");

            await uow.BeginAsync();
            uow.Context.Should().NotBeSameAs(session);
            await uow.RollbackAsync();
        }
        finally
        {
            await client.DropDatabaseAsync(db);
        }
    }

    [Fact]
    public async Task A_failed_commit_ends_the_boundary_and_the_following_rollback_is_a_no_op()
    {
        var client = Client();
        if (client == null) return;
        var db = "birko_task578_" + Guid.NewGuid().ToString("N");
        await client.GetDatabase(db).CreateCollectionAsync("Rows");
        try
        {
            using var uow = new MongoDbUnitOfWork(client);
            await uow.BeginAsync();
            var session = uow.Context!;
            await Write(uow, client, db, "row-1");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            var commit = () => uow.CommitAsync(cancelled.Token);

            await commit.Should().ThrowAsync<OperationCanceledException>("the driver's exception propagates unchanged");
            uow.IsActive.Should().BeFalse();
            uow.Context.Should().BeNull();
            Disposed(session).Should().BeTrue();
            await uow.Invoking(u => u.RollbackAsync()).Should().NotThrowAsync("catch-and-roll-back after a failed commit");
            await uow.Invoking(u => u.RollbackAsync()).Should().ThrowAsync<NoActiveTransactionException>();
            // The transaction is aborted on the server, not left holding its locks until its 60 s lifetime ends: the same
            // row is written outside it at once, which an open transaction would block.
            var rows = client.GetDatabase(db).GetCollection<BsonDocument>("Rows");
            using var quickly = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await rows.InsertOneAsync(new BsonDocument { { "_id", "row-1" } }, cancellationToken: quickly.Token);
            (await rows.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty)).Should().Be(1, "the failed commit wrote nothing");
        }
        finally
        {
            await client.DropDatabaseAsync(db);
        }
    }
}
