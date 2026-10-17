using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Patterns.UnitOfWork;
using Birko.Data.RavenDB.UnitOfWork;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.UnitOfWork;

/// <summary>
/// TASK-658, live RavenDB: a failed cluster-wide commit ends the unit of work, as on every other backend (TASK-325,
/// TASK-578). Measured on HEAD: after a conflict the unit of work stayed active and every retry hit the same conflict;
/// after a cancelled commit it stayed active and a retry wrote the changes late. Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public sealed class Task658RavenUnitOfWorkLiveTests : IDisposable
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly IDocumentStore? _store;
    private readonly string _db = "birko-task658-" + Guid.NewGuid().ToString("N");

    public sealed class Row
    {
        public string? Id { get; set; }
        public int N { get; set; }
    }

    public Task658RavenUnitOfWorkLiveTests(ITestOutputHelper output)
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return;
        }
        _store = new DocumentStore { Urls = new[] { Url! }, Database = _db }.Initialize();
        _store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(_db)));
    }

    public void Dispose()
    {
        if (_store == null) return;
        try { _store.Maintenance.Server.Send(new DeleteDatabasesOperation(_db, hardDelete: true)); } catch { }
        _store.Dispose();
    }

    private async Task<Row?> Load(string id)
    {
        using var session = _store!.OpenAsyncSession();
        return await session.LoadAsync<Row>(id);
    }

    private static async Task ShouldHaveEnded(RavenDbUnitOfWork uow)
    {
        uow.IsActive.Should().BeFalse("a failed commit ends the boundary");
        uow.Context.Should().BeNull();
        await uow.Invoking(u => u.RollbackAsync()).Should().NotThrowAsync("catch-and-roll-back after a failed commit");
        await uow.Invoking(u => u.RollbackAsync()).Should().ThrowAsync<NoActiveTransactionException>();
        await uow.Invoking(u => u.CommitAsync()).Should().ThrowAsync<NoActiveTransactionException>("there is nothing left to retry");
    }

    [Fact]
    public async Task A_conflicting_commit_propagates_and_ends_the_unit_of_work()
    {
        if (_store == null) return;
        using var uow = new RavenDbUnitOfWork(_store);
        await uow.BeginAsync();
        await uow.Context!.StoreAsync(new Row { Id = "rows/1", N = 1 });
        using (var other = _store.OpenAsyncSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
        {
            await other.StoreAsync(new Row { Id = "rows/1", N = 99 });
            await other.SaveChangesAsync();
        }

        await uow.Invoking(u => u.CommitAsync()).Should().ThrowAsync<ConcurrencyException>("the driver's exception, unchanged");

        await ShouldHaveEnded(uow);
        (await Load("rows/1"))!.N.Should().Be(99, "the failed transaction applied nothing");
    }

    [Fact]
    public async Task A_cancelled_commit_ends_the_unit_of_work_and_writes_nothing_later()
    {
        if (_store == null) return;
        using var uow = new RavenDbUnitOfWork(_store);
        await uow.BeginAsync();
        await uow.Context!.StoreAsync(new Row { Id = "rows/2", N = 2 });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await uow.Invoking(u => u.CommitAsync(cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();

        await ShouldHaveEnded(uow);
        (await Load("rows/2")).Should().BeNull("nothing was written, and no retry can write it late");
    }
}
