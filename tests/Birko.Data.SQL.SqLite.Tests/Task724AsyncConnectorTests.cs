using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-724, on SQLite. Measured before the fix: the async <c>CreateTableAsync</c> was a second copy of the sync path that
/// emitted each composite-key column with its own <c>PRIMARY KEY</c> (<c>table has more than one primary key</c>) and never
/// recorded the table in <c>TablesCreated</c>; <c>SelectCountAsync</c> over a list holding a null table threw
/// <c>NullReferenceException</c>; and <c>InsertAsync</c> waited for the write gate with no token, so a cancelled insert
/// queued behind another write waited it out.
/// </summary>
public sealed class Task724AsyncConnectorTests : IDisposable
{
    [Table("T724Composite")]
    public class T724Row
    {
        [PrimaryField]
        public Guid? Guid { get; set; }

        [PrimaryField]
        [RequiredField]
        public DateTime Ts { get; set; }

        public double Value { get; set; }
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t724-{Guid.NewGuid():N}");
    private SqLiteSettings Settings => new(_root, "t724.db");

    public Task724AsyncConnectorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={Path.Combine(_root, "t724.db")}"));
        TestTempDirectory.Delete(_root);
    }

    [Fact]
    public async Task CreateTableAsync_builds_a_composite_key_and_records_the_table()
    {
        var connector = new SqLiteConnector(Settings);

        await connector.CreateTableAsync(new[] { typeof(T724Row) });

        connector.TablesCreated.Should().ContainKey("T724Composite");
        var guid = Guid.NewGuid();
        await connector.InsertAsync(typeof(T724Row), new T724Row { Guid = guid, Ts = new DateTime(2026, 1, 1), Value = 1 });
        await connector.InsertAsync(typeof(T724Row), new T724Row { Guid = guid, Ts = new DateTime(2026, 1, 2), Value = 2 });
        (await connector.SelectCountAsync(new[] { typeof(T724Row) }, (System.Linq.Expressions.LambdaExpression?)null)).Should().Be(2, "one Guid, two times: the key is both columns");
    }

    [Fact]
    public async Task SelectCountAsync_skips_a_null_table()
    {
        var connector = new SqLiteConnector(Settings);
        connector.CreateTable(new[] { typeof(T724Row) });

        var count = await connector.SelectCountAsync(new[] { null!, DataBase.LoadTable(typeof(T724Row)) });

        count.Should().Be(0);
    }

    [Fact]
    public async Task A_cancelled_insert_does_not_wait_for_the_write_gate()
    {
        var connector = new SqLiteConnector(Settings);
        connector.CreateTable(new[] { typeof(T724Row) });
        var release = new TaskCompletionSource();
        var holding = connector.DoCommandAsync(
            command => { command.CommandText = "SELECT 1"; return Task.CompletedTask; },
            async _ => await release.Task,
            isLock: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew();
        _ = Task.Delay(4000).ContinueWith(_ => release.TrySetResult());

        await FluentActions.Awaiting(() => connector.InsertAsync(typeof(T724Row), new T724Row { Guid = Guid.NewGuid(), Ts = DateTime.UtcNow }, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "the cancellation ends the wait for the gate");
        release.TrySetResult();
        await holding;
    }
}
