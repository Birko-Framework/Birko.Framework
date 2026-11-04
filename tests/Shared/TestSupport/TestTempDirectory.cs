global using Birko.Tests.Shared;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Birko.Tests.Shared;

/// <summary>
/// Removes a test's temp directory at teardown, and RECORDS a removal that fails instead of swallowing it (TASK-302).
/// </summary>
/// <remarks>
/// <para>
/// The teardown this replaces was <c>try { Directory.Delete(_root, true); } catch { }</c>. Measured 2026-11-04: one run of
/// <c>Birko.Data.SQL.SqLite.Tests</c> left <b>449</b> directories behind, and <b>every one still held its <c>*.db</c>,
/// <c>-wal</c> and <c>-shm</c></b>: a pooled SQLite connection keeps the file open after the test's own connection is
/// closed, so the delete threw and the <c>catch { }</c> hid it. The machine had accumulated 154,564 such directories.
/// </para>
/// <para>
/// <see cref="Delete"/> therefore (1) clears the SQLite pools of the databases under THAT directory only, (2) retries the
/// delete briefly, and (3) on failure queues the directory. At process exit, when no test can still be running, every
/// SQLite pool is cleared (safe only then: a process-wide clear during the run disposes a parallel sibling's handle,
/// rule 61) and the queue is retried. What still cannot be removed is written to stderr and appended to
/// <see cref="FailureLogPath"/>. A teardown failure never fails a test: the point is a signal, not a gate.
/// </para>
/// <para>
/// SQLite is reached by reflection so this one file links into every test project, with or without
/// <c>Microsoft.Data.Sqlite</c>.
/// </para>
/// </remarks>
internal static class TestTempDirectory
{
    private static readonly ConcurrentQueue<string> Pending = new();
    private static readonly ConcurrentQueue<string> Failures = new();
    private static int _hooked;

    /// <summary>Where unremovable directories are recorded, beside the directories themselves.</summary>
    public static string FailureLogPath => Path.Combine(Path.GetTempPath(), "birko-teardown-failures.log");

    /// <summary>The directories this process could not remove, in the order they were given up on.</summary>
    public static IReadOnlyCollection<string> RecordedFailures => Failures.ToArray();

    /// <summary>Creates a fresh, uniquely named directory under the temp path.</summary>
    public static string Create(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Removes <paramref name="path"/> and everything under it. Never throws. A directory that cannot be removed now is
    /// retried at process exit, and recorded if that fails too.
    /// </summary>
    public static void Delete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (TryDelete(path, ClearSqlitePoolsUnder)) return;

        HookProcessExit();
        Pending.Enqueue(path);
    }

    /// <summary>
    /// The last attempt for one directory: delete, and record it if the delete fails. Exposed so the recording can be
    /// proven (a test holds a file open and asserts the record), without waiting for process exit.
    /// </summary>
    internal static bool DeleteOrRecord(string path)
    {
        if (TryDelete(path, ClearSqlitePoolsUnder)) return true;
        Record(path);
        return false;
    }

    private static bool TryDelete(string path, Action<string> releaseHandles)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (!Directory.Exists(path)) return true;
                if (attempt == 0) releaseHandles(path);
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            Thread.Sleep(25 * (attempt + 1));
        }

        return !Directory.Exists(path);
    }

    private static void Record(string path)
    {
        Failures.Enqueue(path);
        var line = $"{DateTime.Now:O} could not remove {path}";
        try { Console.Error.WriteLine($"[TestTempDirectory] {line}"); } catch { }
        try { File.AppendAllText(FailureLogPath, line + Environment.NewLine); } catch { }
    }

    private static void HookProcessExit()
    {
        if (Interlocked.Exchange(ref _hooked, 1) == 1) return;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DrainAtExit();
    }

    private static void DrainAtExit()
    {
        if (Pending.IsEmpty) return;
        ClearAllSqlitePools();

        while (Pending.TryDequeue(out var path))
        {
            if (!TryDelete(path, _ => { })) Record(path);
        }
    }

    private static readonly Type? SqliteConnectionType =
        Type.GetType("Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite", throwOnError: false);

    /// <summary>Clears the pool of every connection-string shape the framework emits for a <c>*.db</c> under the directory.</summary>
    private static void ClearSqlitePoolsUnder(string path)
    {
        if (SqliteConnectionType == null) return;

        var clearPool = SqliteConnectionType.GetMethod("ClearPool", BindingFlags.Public | BindingFlags.Static);
        if (clearPool == null) return;

        string[] files;
        try { files = Directory.GetFiles(path, "*.db", SearchOption.AllDirectories); }
        catch { return; }

        foreach (var file in files)
        {
            foreach (var cs in ConnectionStringShapes(file))
            {
                try
                {
                    using var probe = (IDisposable)Activator.CreateInstance(SqliteConnectionType, cs)!;
                    clearPool.Invoke(null, new object[] { probe });
                }
                catch
                {
                    // A shape that does not parse is simply not a pool this directory has.
                }
            }
        }
    }

    // SqLiteSettings.GetConnectionString() emits "Data Source={Path}[;Password=…];Default Timeout={CommandTimeout}".
    // The pool key is the whole string, so the common timeouts are tried; a miss is caught at process exit.
    private static IEnumerable<string> ConnectionStringShapes(string file)
    {
        yield return $"Data Source={file}";
        foreach (var t in new[] { 30, 5, 1, 2, 10, 60 })
        {
            yield return $"Data Source={file};Default Timeout={t}";
        }
    }

    private static void ClearAllSqlitePools()
    {
        var clearAll = SqliteConnectionType?.GetMethod("ClearAllPools", BindingFlags.Public | BindingFlags.Static);
        try { clearAll?.Invoke(null, null); } catch { }
    }
}
