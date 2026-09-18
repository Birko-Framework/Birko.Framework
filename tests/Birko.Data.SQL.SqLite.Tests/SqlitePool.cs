using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// Drops the pooled SQLite connections for ONE database, never for the whole process.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Never clear the SQLite pools process-wide from a test.</b> Such a clear drops the pooled
/// connections for every connection string in the process, and xUnit
/// runs test classes in parallel by default (this project sets no <c>CollectionBehavior</c> and no
/// <c>maxParallelThreads</c>). So one class clearing pools in its <c>Dispose</c> disposes the
/// <c>sqlite3</c> handle a <i>sibling</i> class is mid-statement on, and that sibling fails with
/// <c>ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'.</c>
/// The victim is random, always mid-statement, and always passes in isolation — which is TASK-276's
/// signature exactly.
/// </para>
/// <para>
/// <b>Confirmed three independent ways</b> (TASK-276):
/// <list type="bullet">
///   <item><b>Dose-response, this suite</b> — TASK-290 added three classes each calling
///         a process-wide clear in <c>Dispose()</c>: a clean 6-run suite became 1-2 failures per 6 runs,
///         and removing those three calls restored it.</item>
///   <item><b>Consumer Symbio's TASK-657</b> — fourteen consecutive full-suite runs on unchanged source,
///         2 failed, both that exception, in two <i>different</i> classes, with a third originally
///         reported. They built this same helper and a guard.</item>
///   <item><b>The production question is answered</b> — measured 2026-09-08: <b>0</b> calls in the
///         framework's production code and <b>0</b> in any of the 16 consumer repos' production code (the
///         14 consumer hits are all in <c>Symbio.Tests.Unit</c>). So consumers are unaffected and this is
///         test hygiene, not a product defect.</item>
/// </list>
/// </para>
/// <para>
/// ⚠ <b>The pool key is the whole connection string, not the file.</b>
/// <c>SqLiteSettings.GetConnectionString()</c> emits
/// <c>Data Source={Path}[;Password=…];Default Timeout={CommandTimeout}</c>, so two settings over one file
/// with different timeouts are two different pools. Prefer <see cref="ClearFor(SqLiteSettings)"/>, which
/// asks the settings for their own string and cannot guess wrong;
/// <see cref="ClearForDirectory"/> is the best-effort fallback for a fixture that did not retain them, and
/// its limits are stated on it.
/// </para>
/// <para>
/// <b>Why not simply drop the call?</b> Measured 2026-09-08 before choosing: of 400 sampled leaked temp
/// directories, <b>164 still held files</b> — so the pool really does hold handles and a clear really does
/// release them. Dropping it outright would make the leak worse, not neutral. (The leak itself is a
/// separate defect — see [[TASK-302]] — and it is not what this helper is for.)
/// </para>
/// </remarks>
internal static class SqlitePool
{
    /// <summary>Clears the pool for exactly one connection string.</summary>
    public static void ClearFor(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        try
        {
            using var probe = new SqliteConnection(connectionString);
            SqliteConnection.ClearPool(probe);
        }
        catch
        {
            // Best effort: a malformed or already-collected connection string must not fail a teardown.
        }
    }

    /// <summary>Clears the pool for the database these settings name — the precise form.</summary>
    public static void ClearFor(Birko.Data.SQL.SqLite.Stores.SqLiteSettings settings)
    {
        if (settings == null) return;
        ClearFor(settings.GetConnectionString());
    }

    /// <summary>
    /// Best-effort: clears the pools for every <c>*.db</c> file under <paramref name="root"/>, for the
    /// connection-string shapes this framework emits.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>This one can miss, and the miss is quiet.</b> The pool key includes <c>Default Timeout</c>, and
    /// a fixture may use any value, so only the shapes tried here are cleared. It exists for teardowns that
    /// no longer have their settings objects; where a fixture still has them, use
    /// <see cref="ClearFor(SqLiteSettings)"/> instead — it cannot guess wrong. A miss costs a leaked temp
    /// directory, never a wrong test result.
    /// </remarks>
    public static void ClearForDirectory(string root, params int[] commandTimeouts)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

        string[] files;
        try { files = Directory.GetFiles(root, "*.db", SearchOption.AllDirectories); }
        catch { return; }

        // The default plus whatever the caller actually configured.
        var timeouts = new[] { 30 }.Concat(commandTimeouts ?? Array.Empty<int>()).Distinct();

        foreach (var file in files)
        {
            ClearFor($"Data Source={file}");
            foreach (var t in timeouts)
            {
                ClearFor($"Data Source={file};Default Timeout={t}");
            }
        }
    }
}
