using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-296 — <b>does the stale-schema-image read exist on this provider too?</b>
///
/// <para>TASK-290 named it on SQLite: a statement on a pooled <c>sqlite3</c> handle answered from a
/// schema image older than a <c>CREATE TABLE</c> another connection had already committed. TASK-296's
/// remedy (a WAL journal) is SQLite-specific, so the open question was whether an equivalent arises here
/// — where the catalogue is server-side and its visibility is transactional. A "should not arise" is a
/// prediction until measured, and this measures it.</para>
///
/// <para>⚠ It is only askable at all since [[TASK-295]]: before that, <c>RecordTableCreated</c> was
/// called from the base <c>CreateTable</c> which this provider overrides, so <c>TablesCreated</c> was
/// permanently empty here and no escape could ever be classified as the anomaly. A run of this suite
/// against that code would have reported a clean 0 for the wrong reason.</para>
///
/// <para>Same concurrency shape as the SQLite reproduction — the one that matters, established by
/// measurement there: <b>several concurrent callers per table</b> across many distinct cold tables, in
/// waves, rather than one huge burst. A caller released from another's <c>_initLock</c> counts a table
/// whose create is milliseconds old.</para>
/// </summary>
public class ColdTableStormLiveTests : IDisposable
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _out;

    public ColdTableStormLiveTests(ITestOutputHelper output) => _out = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _out.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    /// <remarks>
    /// Gated as a load probe on top of the live gate: it drives 180 concurrent first touches and is a
    /// diagnostic rather than a guard, exactly as the SQLite storm is.
    /// </remarks>
    private bool RequireStorm()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_STORM"))) return true;
        _out.WriteLine("SKIPPED: set BIRKO_STORM to run the cold-table storm.");
        return false;
    }

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    private static void Exec(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void DropAll()
    {
        foreach (var type in PgColdTableProbes.All)
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(type);
            try { Exec($"DROP TABLE IF EXISTS \"{table.Name}\" CASCADE"); } catch { }
        }
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        DropAll();
    }

    private static Func<CancellationToken, Task> MakeProbe(Type type)
        => (Func<CancellationToken, Task>)typeof(ColdTableStormLiveTests)
            .GetMethod(nameof(BuildProbe), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type)
            .Invoke(null, Array.Empty<object>())!;

    private static Func<CancellationToken, Task> BuildProbe<T>() where T : Birko.Data.Models.AbstractModel, new()
    {
        // ONE store per type, shared by every concurrent caller of that type — the production shape.
        var store = new AsyncPostgreSQLStore<T>();
        store.SetSettings(Settings());
        return async ct =>
        {
            await store.CountAsync(null, ct).ConfigureAwait(false);
            await store.ReadAsync(null, null, null, null, ct).ConfigureAwait(false);
        };
    }

    [Fact]
    public async Task TheColdTableStorm_RecordsNoSchemaEscapeOnThisProvider()
    {
        if (!RequireServer()) return;
        if (!RequireStorm()) return;
        DropAll();

        // A connector of its own, so nothing another test did decides what this measures.
        var connector = new PostgreSQLConnector(Settings());
        var probes = PgColdTableProbes.All.Select(MakeProbe).ToArray();

        ThreadPool.GetMinThreads(out var workerMin, out var ioMin);
        ThreadPool.SetMinThreads(Math.Max(workerMin, 256), ioMin);
        var failures = new List<Exception>();
        var sync = new object();
        try
        {
            const int waveSize = 12;
            const int callersPerTable = 3;
            for (var offset = 0; offset < probes.Length; offset += waveSize)
            {
                var wave = probes.Skip(offset).Take(waveSize).ToArray();
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var tasks = wave
                    .SelectMany(p => Enumerable.Repeat(p, callersPerTable))
                    .Select(p => Task.Run(async () =>
                    {
                        await start.Task.ConfigureAwait(false);
                        try { await p(CancellationToken.None).ConfigureAwait(false); }
                        catch (Exception ex) { lock (sync) { failures.Add(ex); } }
                    }))
                    .ToArray();
                start.SetResult();
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }
        finally
        {
            ThreadPool.SetMinThreads(workerMin, ioMin);
        }

        _out.WriteLine($"escapes={connector.SchemaEscapes.Count} generation={connector.SchemaGeneration} "
            + $"failures={failures.Count}");
        foreach (var escape in connector.SchemaEscapes)
        {
            _out.WriteLine($"  ESCAPE [{string.Join(", ", escape.TableNames)}] {escape.Annotation}");
        }
        foreach (var group in failures.GroupBy(x => x.GetType().Name + ": " + x.Message.Split('\n')[0]))
        {
            _out.WriteLine($"  FAIL x{group.Count()} {group.Key}");
        }

        // The stores own the schema-ensure, so the tables must exist afterwards or the storm never
        // reached the condition and a clean result would say nothing.
        using (var conn = new NpgsqlConnection(Settings().GetConnectionString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name LIKE 'PgProbe%'";
            Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(PgColdTableProbes.All.Length,
                "every probe must have schema-ensured");
        }

        connector.SchemaEscapes.Should().BeEmpty(
            "PostgreSQL's catalogue is server-side and its visibility is transactional, so a committed "
            + "CREATE TABLE is visible to every later statement on every connection — the SQLite "
            + "mechanism has no analogue here. Measured rather than predicted, and only askable at all "
            + "since TASK-295 made this provider record its creates");
    }
}
