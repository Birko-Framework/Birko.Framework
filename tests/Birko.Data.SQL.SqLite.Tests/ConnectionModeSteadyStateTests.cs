using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-296 — <b>the steady-state cost of the two candidate remedies, measured on the ordinary case
/// rather than on the storm.</b>
///
/// <para>TASK-290 named the defect (a pooled <c>sqlite3</c> handle answering from a stale schema image)
/// and found two configurations that remove it: <c>Pooling=False</c>, and a WAL journal with pooling left
/// on. Both looked <i>faster</i> under the storm, which is the opposite of the usual assumption about
/// pooling — and precisely why the remedy must not be chosen on a storm number. A storm is dominated by
/// lock contention; the ordinary case is a warm store doing sequential work, which is exactly where
/// opening a real handle per statement should hurt.</para>
///
/// <para>⚠ This is a <b>benchmark</b>, not a guard. It asserts only that each configuration works at all,
/// and prints the timings; a wall-clock assertion would flake on any other machine. The numbers it
/// produced are recorded in TASK-296.</para>
/// </summary>
public class ConnectionModeSteadyStateTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;
    private static int _seq;

    public ConnectionModeSteadyStateTests(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(), $"birko-steady-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    /// <remarks>No process-wide <c>ClearAllPools()</c> — see [[TASK-276]].</remarks>
    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("SteadyRows")]
    public class SteadyRow : AbstractDatabaseModel
    {
        public string? Value { get; set; }
    }

    private sealed class UnpooledSettings : SqLiteSettings
    {
        public UnpooledSettings(string location, string name) : base(location, name) { }
        public override string GetConnectionString() => base.GetConnectionString() + ";Pooling=False";
    }

    private static bool RequireBenchmark(ITestOutputHelper output)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_STORM")))
        {
            return true;
        }
        output.WriteLine("SKIPPED: set BIRKO_STORM to run this benchmark.");
        return false;
    }

    /// <param name="wal">Set the journal mode to WAL before the run. It is persistent in the file.</param>
    private async Task<TimeSpan> Measure(SqLiteSettings settings, bool wal, int operations)
    {
        if (wal)
        {
            using var setup = new SqliteConnection(settings.GetConnectionString());
            setup.Open();
            using var cmd = setup.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL";
            Convert.ToString(cmd.ExecuteScalar()).Should().Be("wal");
        }

        var store = new AsyncSQLiteStore<SteadyRow>();
        store.SetSettings(settings);

        // Warm: the schema-ensure and the first connection are not what this measures.
        await store.CreateAsync(new SteadyRow { Guid = Guid.NewGuid(), Value = "warm" });
        await store.CountAsync();

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < operations; i++)
        {
            await store.CreateAsync(new SteadyRow { Guid = Guid.NewGuid(), Value = $"v{i}" });
            await store.CountAsync();
            await store.ReadAsync(x => x.Value == $"v{i}", null, null, null, default);
        }
        clock.Stop();

        (await store.CountAsync()).Should().Be(operations + 1, "the run must actually have done the work");
        return clock.Elapsed;
    }

    [Fact]
    public async Task SteadyState_PooledDeleteJournal_versus_Unpooled_versus_Wal()
    {
        if (!RequireBenchmark(_out)) return;
        const int operations = 200;   // 200 x (write + count + filtered read), sequential, warm

        var pooled = await Measure(
            new SqLiteSettings(_root, $"steady{Interlocked.Increment(ref _seq)}.db"), wal: false, operations);
        var unpooled = await Measure(
            new UnpooledSettings(_root, $"steady{Interlocked.Increment(ref _seq)}.db"), wal: false, operations);
        var walPooled = await Measure(
            new SqLiteSettings(_root, $"steady{Interlocked.Increment(ref _seq)}.db"), wal: true, operations);

        _out.WriteLine($"operations per configuration: {operations} x (write + count + filtered read)");
        _out.WriteLine($"  pooled + delete journal (shipped default) : {pooled.TotalMilliseconds:N0} ms");
        _out.WriteLine($"  UNPOOLED + delete journal                 : {unpooled.TotalMilliseconds:N0} ms"
            + $"  ({unpooled.TotalMilliseconds / pooled.TotalMilliseconds:N2}x)");
        _out.WriteLine($"  pooled + WAL                              : {walPooled.TotalMilliseconds:N0} ms"
            + $"  ({walPooled.TotalMilliseconds / pooled.TotalMilliseconds:N2}x)");
    }
}
