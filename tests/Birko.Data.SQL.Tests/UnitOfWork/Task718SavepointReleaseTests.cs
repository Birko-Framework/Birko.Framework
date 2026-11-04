using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.UnitOfWork;

/// <summary>
/// TASK-718. Measured before the fix: when <c>SqlSavepoint</c>'s <c>Release</c> threw after the write had applied, the
/// write was reported as failed — and the retrying callers this producer exists for would run it again inside the same
/// transaction. An unreleased savepoint ends with its transaction; the write stands.
/// </summary>
public class Task718SavepointReleaseTests
{
    private sealed class ReleaseFails : DbTransaction
    {
        public List<string> Calls { get; } = new();
        protected override DbConnection? DbConnection => null;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public override bool SupportsSavepoints => true;
        public override void Commit() { }
        public override void Rollback() { }
        public override void Save(string savepointName) => Calls.Add("save");
        public override void Rollback(string savepointName) => Calls.Add("rollback");
        public override void Release(string savepointName)
        {
            Calls.Add("release");
            throw new InvalidOperationException("release refused");
        }
    }

    [Fact]
    public void A_failed_release_after_an_applied_write_is_not_the_write_failing()
    {
        var transaction = new ReleaseFails();
        var writes = 0;

        FluentActions.Invoking(() => SqlSavepoint.Run(transaction, () => writes++)).Should().NotThrow();

        writes.Should().Be(1);
        transaction.Calls.Should().Equal("save", "release");
    }

    [Fact]
    public async Task A_failed_async_release_after_an_applied_write_is_not_the_write_failing()
    {
        var transaction = new ReleaseFails();
        var writes = 0;

        await FluentActions.Awaiting(() => SqlSavepoint.RunAsync(transaction, () => { writes++; return Task.CompletedTask; }, CancellationToken.None))
            .Should().NotThrowAsync();

        writes.Should().Be(1);
        transaction.Calls.Should().Equal("save", "release");
    }

    [Fact]
    public void A_write_that_throws_still_rolls_back_and_throws()
    {
        var transaction = new ReleaseFails();

        FluentActions.Invoking(() => SqlSavepoint.Run(transaction, () => throw new TimeoutException("write")))
            .Should().Throw<TimeoutException>();

        transaction.Calls.Should().Equal("save", "rollback");
    }
}
