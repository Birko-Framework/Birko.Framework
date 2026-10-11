using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Birko.Data.Patterns.UnitOfWork;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.UnitOfWork;

/// <summary>
/// SH-M427 / SH-M428 (TASK-325) — <c>SqlUnitOfWork</c> released its connection only on the success path.
/// <list type="bullet">
/// <item>A failed <c>OpenAsync</c> / <c>BeginTransactionAsync</c> left the connection assigned with <c>IsActive</c> false,
/// and a retried <c>BeginAsync</c> overwrote it — one leaked pooled connection per failed attempt.</item>
/// <item>A failed <c>CommitAsync</c> (a deadlock victim, error 1205, reproduced live by TASK-306) or <c>RollbackAsync</c>
/// skipped cleanup, holding the connection and an uncompletable transaction until <c>Dispose</c>.</item>
/// </list>
/// Releasing on failure must not cost the caller the original exception: the idiomatic
/// <c>catch { await uow.RollbackAsync(); throw; }</c> after a failed commit is a no-op, not a
/// <c>NoActiveTransactionException</c> that replaces the deadlock.
///
/// <para>The assertions are on observed state — the connection's and transaction's disposal, and which exception
/// reached the caller — never on the absence of an exception.</para>
/// </summary>
public class SqlUnitOfWorkFailureReleaseTests
{
    private sealed class FakeTransaction : DbTransaction
    {
        private readonly FakeConnection _connection;
        public FakeTransaction(FakeConnection connection) => _connection = connection;
        public bool Disposed { get; private set; }
        protected override DbConnection DbConnection => _connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public override void Commit()
        {
            if (_connection.FailCommit) throw new InvalidOperationException("deadlock victim (1205)");
        }
        public override void Rollback()
        {
            if (_connection.FailRollback) throw new InvalidOperationException("rollback failed");
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FakeConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        public bool FailOpen { get; set; }
        public bool FailBegin { get; set; }
        public bool FailCommit { get; set; }
        public bool FailRollback { get; set; }
        public bool Disposed { get; private set; }
        public FakeTransaction? Transaction { get; private set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open()
        {
            if (FailOpen) throw new InvalidOperationException("server unreachable");
            _state = ConnectionState.Open;
        }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            if (FailBegin) throw new InvalidOperationException("begin refused");
            return Transaction = new FakeTransaction(this);
        }
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            _state = ConnectionState.Closed;
            base.Dispose(disposing);
        }
    }

    private sealed class FakeConnector : AbstractConnector
    {
        public FakeConnector() : base(new Birko.Configuration.PasswordSettings()) { }
        public List<FakeConnection> Created { get; } = new();
        public Action<FakeConnection>? Configure { get; set; }

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
        {
            var connection = new FakeConnection();
            Configure?.Invoke(connection);
            Created.Add(connection);
            return connection;
        }
        public override string ConvertType(DbType type, Birko.Data.SQL.Fields.AbstractField field) => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field) => throw new NotSupportedException();
    }

    private static (SqlUnitOfWork uow, FakeConnector connector) Create(Action<FakeConnection>? configure = null)
    {
        var connector = new FakeConnector { Configure = configure };
        return (new SqlUnitOfWork(connector, new Birko.Configuration.PasswordSettings()), connector);
    }

    [Fact]
    public async Task A_failed_open_releases_the_connection()
    {
        var (uow, connector) = Create(c => c.FailOpen = true);
        using var _ = uow;

        await FluentActions.Awaiting(() => uow.BeginAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("server unreachable");

        connector.Created.Should().ContainSingle().Which.Disposed.Should().BeTrue("a failed open must not hold the connection until Dispose");
        uow.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_begin_releases_the_opened_connection()
    {
        var (uow, connector) = Create(c => c.FailBegin = true);
        using var _ = uow;

        await FluentActions.Awaiting(() => uow.BeginAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("begin refused");

        connector.Created.Should().ContainSingle().Which.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Retried_failed_begins_leak_no_connection()
    {
        var (uow, connector) = Create(c => c.FailOpen = true);
        using var _ = uow;

        for (var i = 0; i < 3; i++)
        {
            await FluentActions.Awaiting(() => uow.BeginAsync()).Should().ThrowAsync<InvalidOperationException>();
        }

        connector.Created.Should().HaveCount(3).And.OnlyContain(c => c.Disposed,
            "each failed attempt releases its own connection — the field used to be overwritten, stranding the previous one");
    }

    [Fact]
    public async Task A_failed_commit_releases_the_transaction_and_the_connection()
    {
        var (uow, connector) = Create(c => c.FailCommit = true);
        using var _ = uow;
        await uow.BeginAsync();

        await FluentActions.Awaiting(() => uow.CommitAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*1205*");

        var connection = connector.Created.Should().ContainSingle().Subject;
        connection.Transaction!.Disposed.Should().BeTrue("disposing an uncommitted transaction rolls it back");
        connection.Disposed.Should().BeTrue("the connection goes back to the pool instead of waiting for Dispose");
        uow.IsActive.Should().BeFalse();
        uow.Context.Should().BeNull();
    }

    [Fact]
    public async Task Catch_and_roll_back_after_a_failed_commit_keeps_the_commits_exception()
    {
        var (uow, _) = Create(c => c.FailCommit = true);
        using var __ = uow;
        await uow.BeginAsync();

        async Task Work()
        {
            try
            {
                await uow.CommitAsync();
            }
            catch
            {
                await uow.RollbackAsync();
                throw;
            }
        }

        await FluentActions.Awaiting(Work).Should().ThrowAsync<InvalidOperationException>().WithMessage("*1205*",
            "the rollback after a failed commit is a no-op — a NoActiveTransactionException here would hide the deadlock");
    }

    [Fact]
    public async Task The_no_op_rollback_is_spent_once()
    {
        var (uow, _) = Create(c => c.FailCommit = true);
        using var __ = uow;
        await uow.BeginAsync();
        await FluentActions.Awaiting(() => uow.CommitAsync()).Should().ThrowAsync<InvalidOperationException>();

        await uow.RollbackAsync();

        await FluentActions.Awaiting(() => uow.RollbackAsync()).Should().ThrowAsync<NoActiveTransactionException>(
            "only the rollback that answers the failed commit is excused");
    }

    [Fact]
    public async Task A_failed_rollback_releases_the_connection()
    {
        var (uow, connector) = Create(c => c.FailRollback = true);
        using var _ = uow;
        await uow.BeginAsync();

        await FluentActions.Awaiting(() => uow.RollbackAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("rollback failed");

        connector.Created.Should().ContainSingle().Which.Disposed.Should().BeTrue();
        uow.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task A_unit_of_work_can_begin_again_after_a_failed_begin()
    {
        var attempt = 0;
        var (uow, connector) = Create(c => c.FailOpen = attempt++ == 0);
        using var _ = uow;
        await FluentActions.Awaiting(() => uow.BeginAsync()).Should().ThrowAsync<InvalidOperationException>();

        await uow.BeginAsync();

        uow.IsActive.Should().BeTrue();
        await uow.CommitAsync();
        connector.Created.Should().HaveCount(2).And.OnlyContain(c => c.Disposed);
    }

    [Fact]
    public async Task Rollback_without_a_transaction_still_throws()
    {
        // Contract pin: the no-op is only for the rollback that answers a failed commit.
        var (uow, _) = Create();
        using var __ = uow;

        await FluentActions.Awaiting(() => uow.RollbackAsync()).Should().ThrowAsync<NoActiveTransactionException>();
    }

    [Fact]
    public async Task A_successful_commit_releases_the_connection()
    {
        // Contract pin: the success path already cleaned up.
        var (uow, connector) = Create();
        using var _ = uow;
        await uow.BeginAsync();

        await uow.CommitAsync();

        connector.Created.Should().ContainSingle().Which.Disposed.Should().BeTrue();
    }
}
