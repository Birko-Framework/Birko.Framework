using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Birko.Data.SQL.PostgreSQL.View.Tests;

/// <summary>
/// TASK-216 — a filtered DELETE / UPDATE qualifies its <c>WHERE</c> with the bare table name while the
/// statement's target table is quoted, so on PostgreSQL the qualifier folds and matches nothing.
///
/// <para>
/// Sibling of <see cref="PostgreSqlOnTheFlyViewTests"/> (TASK-211), which fixed the identical mechanism on
/// the READ path with a bare alias in the <c>FROM</c> clause. That trick does not port to writes — MSSql
/// rejects <c>DELETE FROM t AS a</c> — so a write drops the qualifier instead, which it can afford to do
/// because it targets exactly one table.
/// </para>
///
/// <para>
/// Gated on a live server: set <c>BIRKO_PG_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> /
/// <c>_DB</c>). These have to run against a folding provider — SQLite, MySQL and MSSql are all
/// case-insensitive for identifiers, so no offline suite can tell the fix from the defect.
/// </para>
/// </summary>
public class PostgreSqlFilteredWriteTests : IDisposable
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    /// <summary>
    /// Whether a live PostgreSQL is configured — throwing instead when the run REQUIRES one.
    /// </summary>
    /// <remarks>
    /// ⚠ TASK-479: <c>Skip.IfNot(Server, …)</c> alone reports <b>Skipped</b>, which is honest and
    /// visible — unlike the silent <c>return;</c> gates that task found elsewhere — but it still leaves
    /// the JOB green. <c>live-tests.yml</c> declares <c>BIRKO_REQUIRE_LIVE=1</c> precisely so a container
    /// that never came up cannot be mistaken for a clean run, and a skip is exactly that mistake.
    /// Ordering matters: the throw fires first for a required run, and the skip still works for a
    /// developer without a server.
    /// </remarks>
    private static bool Server
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Host))
            {
                return true;
            }
            if (RequireLive)
            {
                throw new InvalidOperationException(
                    "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                    + "BIRKO_REQUIRE_LIVE is set, so its absence is a failure.");
            }
            return false;
        }
    }

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    public void Dispose()
    {
        if (!Server) return;
        try { Exec("DROP TABLE IF EXISTS \"FwPeople\" CASCADE"); } catch { }
    }

    public class FwPerson : AbstractModel
    {
        public string? Name { get; set; }
        public int Score { get; set; }
        public DateTime? Seen { get; set; }
    }

    private sealed class FwPersonMapping : IModelMapping<FwPerson>
    {
        public void Configure(ModelMap<FwPerson> map)
        {
            map.ToTable("FwPeople").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Score);
            map.Property(x => x.Seen);
        }
    }

    private static PostgreSQLConnector Seed()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new FwPersonMapping());
        registry.ApplyToDatabase();

        var connector = new PostgreSQLConnector(Settings());
        Exec("DROP TABLE IF EXISTS \"FwPeople\" CASCADE");
        connector.CreateTable(new[] { typeof(FwPerson) });

        foreach (var (name, score) in new[] { ("a", 1), ("b", 2), ("c", 3) })
        {
            connector.Insert(typeof(FwPerson), new FwPerson
            {
                Guid = Guid.NewGuid(),
                Name = name,
                Score = score,
                Seen = new DateTime(2026, 8, 15, 12, 0, 0),
            });
        }
        return connector;
    }

    private static void Exec(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static List<string> NamesLeft(PostgreSQLConnector connector)
        => connector.Select(typeof(FwPerson), (IEnumerable<Conditions.Condition>?)null)
            .Cast<FwPerson>().Select(p => p.Name!).OrderBy(x => x).ToList();

    // ── the filed defect ──

    [SkippableFact]
    public void A_filtered_delete_removes_only_the_matching_row()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        var connector = Seed();
        Expression<Func<FwPerson, bool>> filter = p => p.Name == "a";

        connector.Delete(typeof(FwPerson), DataBase.ParseConditionExpression(filter));

        NamesLeft(connector).Should().Equal("b", "c");
    }

    [SkippableFact]
    public void A_filtered_update_changes_only_the_matching_row()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        var connector = Seed();
        Expression<Func<FwPerson, bool>> filter = p => p.Name == "a";

        // The fields/values pair is built the way the store layer builds it (DataBaseBulkStore.UpdateInternal
        // — both from the same assignments). NOT via Update(Table, values, conditions): that overload builds
        // its SET list from EVERY column while the parameters come from the caller's subset, so it emits
        // unbound placeholders and fails for a reason that has nothing to do with this task. Measured and
        // filed as TASK-217; asserting it here would have encoded a second defect into this task's evidence.
        connector.Update(
            "FwPeople",
            new Dictionary<int, string> { { 0, "Score" } },
            new Dictionary<string, object> { { "Score", 99 } },
            DataBase.ParseConditionExpression(filter));

        var scores = connector.Select(typeof(FwPerson), (IEnumerable<Conditions.Condition>?)null)
            .Cast<FwPerson>().OrderBy(p => p.Name).Select(p => p.Score).ToList();
        scores.Should().Equal(99, 2, 3);
    }

    // ── the shape a partial fix misses: the qualifier arrives function-wrapped ──

    [SkippableFact]
    public void A_filtered_delete_whose_column_is_wrapped_in_a_function_works()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        // Renders LOWER(FwPeople.Name) — a qualifier inside a function call. Rewriting condition names one
        // at a time misses this, and a missed producer is the same failure the whole family keeps hitting.
        var connector = Seed();
        Expression<Func<FwPerson, bool>> filter = p => p.Name!.ToLower() == "a";

        connector.Delete(typeof(FwPerson), DataBase.ParseConditionExpression(filter));

        NamesLeft(connector).Should().Equal("b", "c");
    }

    [SkippableFact]
    public void A_filtered_delete_over_a_date_truncated_column_works()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        // Renders DATE(FwPeople.Seen) — the .Date rewrite (TASK-196), the second wrapping shape.
        var connector = Seed();
        Expression<Func<FwPerson, bool>> filter = p => p.Seen!.Value.Date == new DateTime(2026, 8, 15);

        connector.Delete(typeof(FwPerson), DataBase.ParseConditionExpression(filter));

        NamesLeft(connector).Should().BeEmpty();
    }

    // ── criterion 4: the two read-side sinks that share the condition renderer ──

    [SkippableFact]
    public void A_filtered_count_returns_the_right_number()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        // SelectCount goes through CreateSelectCommand, so TASK-211's alias should already cover it.
        // Confirmed here rather than reasoned — this task must not leave a fourth sink to rediscover.
        var connector = Seed();
        Expression<Func<FwPerson, bool>> filter = p => p.Score > 1;

        var count = connector.SelectCount(
            typeof(FwPerson),
            DataBase.ParseConditionExpression(filter));

        count.Should().Be(2);
    }

    // ── the guard TASK-109 installed must survive the rewrite ──

    [SkippableFact]
    public void An_unbounded_delete_is_still_refused()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        var connector = Seed();

        var deleteEverything = () => connector.Delete(typeof(FwPerson), conditions: null);

        deleteEverything.Should().Throw<Data.Exceptions.WholeTableWriteException>(
            "stripping the qualifier must not disturb the whole-table write guard, which decides on the "
            + "rendered clause being empty");
        NamesLeft(connector).Should().Equal("a", "b", "c");
    }
}
