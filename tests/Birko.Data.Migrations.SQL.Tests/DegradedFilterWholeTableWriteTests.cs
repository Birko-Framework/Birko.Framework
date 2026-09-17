using System;
using System.Collections.Generic;
using System.IO;
using Birko.Data.Exceptions;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// <b>SH-H032 (TASK-314).</b> <see cref="SqlDataMigrator.ParseFilterToWhere"/> returns an empty clause for
/// two unrelated inputs, and every caller appended the <c>WHERE</c> only when the clause was non-empty:
/// <list type="bullet">
/// <item>no filter at all — <c>null</c>, whitespace, <c>"{}"</c> — a deliberate match-all; and</item>
/// <item>a filter that named fields and produced no terms, e.g. <c>{"status":{}}</c>, where the object
/// branch is taken and the inner operator loop adds nothing.</item>
/// </list>
/// The second is a typo and it emitted <c>DELETE FROM "T"</c> with no <c>WHERE</c>.
///
/// <para>
/// <b>These assert observed rows, never "it did not throw".</b> The acceptance criterion for a silent-loss
/// claim is the state left behind, and § Conventions records several defects that a no-exception assertion
/// hid. Each test counts what survived on a real file-backed SQLite database. A <c>"did not throw"</c>
/// version of the delete test passes against the defect, because the defect's whole nature is that it
/// succeeded.
/// </para>
/// </summary>
public class DegradedFilterWholeTableWriteTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqlDataMigrator _migrator;
    private readonly SqliteConnection _connection;

    public DegradedFilterWholeTableWriteTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"birko-sh-h032-{Guid.NewGuid():N}.db");
        var settings = new SqLiteSettings(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath));
        var connector = DataBase.GetConnector<SqLiteConnector>(settings);

        _connection = new SqliteConnection(settings.GetConnectionString());
        _connection.Open();
        Exec("CREATE TABLE Widgets (Id INTEGER PRIMARY KEY, Name TEXT, status TEXT)");
        Exec("INSERT INTO Widgets (Id, Name, status) VALUES (1, 'a', 'active')");
        Exec("INSERT INTO Widgets (Id, Name, status) VALUES (2, 'b', 'archived')");
        Exec("INSERT INTO Widgets (Id, Name, status) VALUES (3, 'c', 'archived')");

        _migrator = new SqlDataMigrator(_connection, null, connector);
    }

    private void Exec(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Counted on the raw connection, so it cannot inherit the migrator's own filtering.</summary>
    private long RowCount(string where = "1 = 1")
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM Widgets WHERE {where}";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        _connection.Dispose();
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_dbPath}"));
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch
        {
            // Best-effort temp cleanup; a leaked temp file must not fail the run.
        }
    }

    // ---------------------------------------------------------------- the defect

    [Theory]
    [InlineData("{\"status\":{}}")]
    [InlineData("{\"status\":{},\"Name\":{}}")]
    public void A_filter_whose_every_term_is_dropped_does_not_delete_the_table(string filterJson)
    {
        Action act = () => _migrator.DeleteDocuments("Widgets", filterJson);

        act.Should().Throw<WholeTableWriteException>()
            .Which.Operation.Should().Be("delete");

        // The assertion that matters: nothing was destroyed. Before the fix this was 0.
        RowCount().Should().Be(3);
    }

    [Fact]
    public void A_filter_whose_every_term_is_dropped_does_not_rewrite_every_row()
    {
        Action act = () => _migrator.UpdateDocuments(
            "Widgets", "{\"status\":{}}", new Dictionary<string, object> { ["Name"] = "OVERWRITTEN" });

        act.Should().Throw<WholeTableWriteException>()
            .Which.Operation.Should().Be("update");

        // Before the fix all three rows read back as 'OVERWRITTEN'.
        RowCount("Name = 'OVERWRITTEN'").Should().Be(0);
        RowCount("Name IN ('a','b','c')").Should().Be(3);
    }

    /// <summary>
    /// The read is guarded on the same terms, so a count cannot quietly answer for the whole table while a
    /// delete built from the identical filter is refused (§ TASK-215 — guard the whole verb family;
    /// § TASK-313 — a destructive statement must not select a different set than its own read).
    /// </summary>
    [Fact]
    public void A_filter_whose_every_term_is_dropped_does_not_count_the_whole_table()
    {
        Action act = () => _migrator.CountDocuments("Widgets", "{\"status\":{}}");

        act.Should().Throw<WholeTableWriteException>()
            .Which.Operation.Should().Be("count");
    }

    /// <summary>The refusal has to name a door this caller can actually take (§ SH-H037).</summary>
    [Fact]
    public void The_refusal_names_the_deliberate_door_and_not_a_predicate()
    {
        Action act = () => _migrator.DeleteDocuments("Widgets", "{\"status\":{}}");

        var message = act.Should().Throw<WholeTableWriteException>().Which.Message;

        message.Should().Contain("Widgets");
        message.Should().Contain("{}");
        // A JSON-filter caller has no expression tree, so offering one would point at a door that does
        // not exist here — the very defect WholeTableWriteException's own remarks warn about.
        message.Should().NotContain("x => true");
    }

    // ---------------------------------------------------------------- contract pins
    // These pass both before and after the fix. They are here to prove the guard is not too broad —
    // without them, "refuse every filter" would look like a valid fix.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void The_explicit_match_all_door_still_deletes_everything(string? filterJson)
    {
        _migrator.DeleteDocuments("Widgets", filterJson!);

        RowCount().Should().Be(0);
    }

    [Fact]
    public void An_ordinary_filter_still_deletes_only_what_it_names()
    {
        _migrator.DeleteDocuments("Widgets", "{\"status\":\"archived\"}");

        RowCount().Should().Be(1);
        RowCount("status = 'active'").Should().Be(1);
    }

    [Fact]
    public void An_ordinary_operator_filter_still_updates_only_what_it_names()
    {
        _migrator.UpdateDocuments(
            "Widgets", "{\"Id\":{\"$gt\":2}}", new Dictionary<string, object> { ["Name"] = "high" });

        RowCount("Name = 'high'").Should().Be(1);
    }

    [Fact]
    public void An_ordinary_filter_still_counts_only_what_it_names()
    {
        _migrator.CountDocuments("Widgets", "{\"status\":\"archived\"}").Should().Be(2);
    }

    [Fact]
    public void A_count_with_no_filter_still_counts_everything()
    {
        _migrator.CountDocuments("Widgets").Should().Be(3);
    }
}
