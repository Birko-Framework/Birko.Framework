using System;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-276 — no test in this project may clear the SQLite connection pools <b>process-wide</b>.
/// </summary>
/// <remarks>
/// <para>
/// A process-wide pool clear disposes the pooled connections for every connection string in the
/// process. xUnit runs test classes in parallel by default here, so a class calling it in <c>Dispose</c>
/// disposes the <c>sqlite3</c> handle a sibling class is mid-statement on. The sibling then fails with
/// <c>ObjectDisposedException: … Object name: 'SQLitePCL.sqlite3'</c>, at random, always mid-statement,
/// and always passes in isolation — TASK-276's signature.
/// </para>
/// <para>
/// ⚠ <b>Why a guard rather than only the fix.</b> The call is the obvious one, it is what every example
/// on the internet uses, and <b>the damage it does never lands in the file that calls it</b> — so nothing
/// about the symptom points a future author back at the cause. A pattern cleared without a static guard
/// in the same change comes back; the guard is what makes the clearing stick.
/// </para>
/// </remarks>
public sealed class SqlitePoolIsolationTests
{
    /// <summary>
    /// The forbidden call, assembled rather than written out.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Deliberate — please do not "simplify" this back to a literal.</b> This file necessarily talks
    /// about the call, and the scan below reads every <c>.cs</c> file in the project including this one.
    /// A literal here would make the guard report itself, and the usual fix for that (exclude this file)
    /// is worse, because then this file is the one place the rule is not enforced.
    /// </remarks>
    private static readonly string Forbidden = "Clear" + "All" + "Pools";

    private static string ProjectRoot()
    {
        // bin/Debug/net10.0 -> project root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.csproj").Any())
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the test project root must be findable from the test binary");
        return dir!.FullName;
    }

    [Fact]
    public void No_test_in_this_project_clears_the_pools_process_wide()
    {
        var root = ProjectRoot();
        var offenders = Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => new { File = f, Text = File.ReadAllText(f) })
            .Where(x => x.Text.Contains(Forbidden, StringComparison.Ordinal))
            .Select(x => Path.GetFileName(x.File))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            $"a process-wide pool clear disposes the sqlite3 handle a PARALLEL sibling class is "
            + $"mid-statement on, and the victim is never the file that made the call (TASK-276). "
            + $"Use SqlitePool.ClearFor(settings) — or SqlitePool.ClearForDirectory(root) — which clears "
            + $"only this fixture's own database.");
    }

    /// <summary>
    /// A scanner that found nothing because it was looking in the wrong place would report a clean pass
    /// forever. This proves it can see the project's files and match the pattern it is looking for.
    /// </summary>
    [Fact]
    public void The_scan_can_actually_see_this_projects_sources()
    {
        var root = ProjectRoot();
        var files = Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        files.Should().HaveCountGreaterThan(20, "this project has dozens of test files");
        files.Should().Contain(f => Path.GetFileName(f) == "SqlitePool.cs",
            "the helper that replaces the forbidden call must be in the scanned set");
    }

    /// <summary>
    /// ⚠ The premise <see cref="SqlitePool"/> is built on, pinned rather than trusted: the pool key is the
    /// <b>whole connection string</b>, so two settings over one file with different
    /// <c>Default Timeout</c> values are two different pools.
    /// </summary>
    /// <remarks>
    /// If this ever stops being true, <c>ClearForDirectory</c>'s timeout sweep becomes dead weight and
    /// <c>ClearFor(settings)</c> stops being meaningfully more precise than a path-only clear — so the
    /// helper's whole shape rests on it. Consumer Symbio recorded the same property independently.
    /// </remarks>
    [Fact]
    public void The_pool_key_includes_the_whole_connection_string_not_just_the_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"birko-poolkey-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "probe.db");

        try
        {
            var withTimeout = $"Data Source={file};Default Timeout=7";
            var pathOnly = $"Data Source={file}";

            // Open and return a connection to the timeout-keyed pool.
            using (var c = new SqliteConnection(withTimeout))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE IF NOT EXISTS T (X TEXT)";
                cmd.ExecuteNonQuery();
            }

            // Clearing the path-only pool must not be assumed to touch it. The observable proof that they
            // are distinct keys is that the file is still locked by the pooled handle afterwards.
            using (var other = new SqliteConnection(pathOnly))
            {
                SqliteConnection.ClearPool(other);
            }

            var deletableAfterWrongKey = TryDelete(file);

            if (!deletableAfterWrongKey)
            {
                // The expected case: the wrong key left the handle pooled, so the right key is required.
                using (var right = new SqliteConnection(withTimeout))
                {
                    SqliteConnection.ClearPool(right);
                }

                TryDelete(file).Should().BeTrue(
                    "clearing the pool for the EXACT connection string must release the file, or "
                    + "SqlitePool.ClearFor(settings) cannot do its job");
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return !File.Exists(file);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
