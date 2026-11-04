using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Patterns.UnitOfWork;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-718, on SQLite. Measured before the fix: <c>SqlUnitOfWork.CommitAsync</c> looked for the rollback-only mark through
/// <c>AmbientSqlTransaction.Find</c> in the flow that called it, not on the entry it published, so an owner committed
/// from a flow that does not carry its cell committed over a participant's rollback.
/// </summary>
public sealed class Task718UnitOfWorkRollbackOnlyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t718-{Guid.NewGuid():N}");

    public Task718UnitOfWorkRollbackOnlyTests() => Directory.CreateDirectory(_root);

    private SqLiteSettings Settings() => new(_root, "uow.db");

    private long Rows(SqLiteSettings settings)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_root, "uow.db")}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM t718";
        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task An_owner_committed_from_another_flow_still_honours_a_participant_s_rollback()
    {
        var settings = Settings();
        var connector = new SqLiteConnector(settings);
        using (var setup = new SqliteConnection($"Data Source={Path.Combine(_root, "uow.db")}"))
        {
            setup.Open();
            using var create = setup.CreateCommand();
            create.CommandText = "CREATE TABLE t718 (x INTEGER)";
            create.ExecuteNonQuery();
        }

        await using var owner = new SqlUnitOfWork(connector, settings);
        await owner.BeginAsync();
        using (var insert = owner.Context!.Connection.CreateCommand())
        {
            insert.Transaction = owner.Context.Transaction;
            insert.CommandText = "INSERT INTO t718 (x) VALUES (1)";
            insert.ExecuteNonQuery();
        }

        await using (var participant = new SqlUnitOfWork(connector, settings))
        {
            await participant.BeginAsync();
            participant.IsParticipant.Should().BeTrue();
            await participant.RollbackAsync();
        }

        // A flow that does not carry the owner's cell: a callback queued without the execution context.
        Task commit;
        using (ExecutionContext.SuppressFlow())
        {
            commit = Task.Run(() => owner.CommitAsync());
        }

        await FluentActions.Awaiting(() => commit).Should().ThrowAsync<TransactionRollbackOnlyException>();
        await owner.RollbackAsync();
        Rows(settings).Should().Be(0);
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={Path.Combine(_root, "uow.db")}"));
        TestTempDirectory.Delete(_root);
    }
}
