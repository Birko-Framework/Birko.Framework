using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using Birko.Models.SQL.Mapping;
using PortableViewQueryMode = Birko.Data.Views.ViewQueryMode;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Birko.Data.SQL.PostgreSQL.View.Tests;

/// <summary>
/// TASK-209 — SQL views were comprehensively broken on PostgreSQL, and only PostgreSQL, because it is the
/// one supported provider that <b>case-folds</b> an unquoted identifier.
///
/// <para>
/// <b>Why no test ever caught it.</b> Every end-to-end view test in this family runs on SQLite, which is
/// case-insensitive for identifiers; MySQL and MSSql are too under their default collations. So the whole
/// suite was green against SQL that could not execute on the one provider that cares. The task that filed
/// this required a real-PostgreSQL reproduction *before* any fix, on the grounds that otherwise it "cannot
/// distinguish a fix from a no-op" — and it was right: the filed defect was the <b>third</b> of four
/// failures, so fixing it alone would have changed nothing observable.
/// </para>
///
/// <para>
/// <b>The root fact.</b> <c>CreateTable</c> quotes the TABLE and emits COLUMN definitions bare, so on
/// PostgreSQL every base column folds to lower case (measured: <c>avpersons.name</c>, <c>avorders.personid</c>).
/// A column reference must therefore be bare to resolve, and a table reference quoted. Four sinks
/// disagreed, each failing before the next became reachable:
/// </para>
/// <list type="number">
/// <item>view DDL SELECT list emitted <c>AvPersons.Name</c> — table unquoted →
/// <c>missing FROM-clause entry for table "avpersons"</c></item>
/// <item>view DDL <c>JOIN ON</c> emitted <c>"AvOrders"."PersonId"</c> — column quoted →
/// <c>column AvOrders.PersonId does not exist</c></item>
/// <item>persistent read emitted <c>SELECT "Name"</c> → <c>column "Name" does not exist</c> — the filed defect</item>
/// <item>the aggregate DDL alias was quoted, so it had to move with (3)</item>
/// </list>
///
/// <para>
/// All four now follow one rule — <b>quote tables, never quote columns</b> — which is what
/// <c>CLAUDE.md § Conventions</c> already said and what the persistent <c>ORDER BY</c> already did.
/// </para>
///
/// <para>
/// <b>Scope, stated so a green run is not read as more than it is.</b> These cover the <b>persistent</b>
/// path. The <b>on-the-fly</b> path — and, as it turned out, every ordinary entity read — was broken by the
/// same qualifier mechanism through a different builder, and is closed by TASK-211 with executing
/// assertions in <see cref="PostgreSqlOnTheFlyViewTests"/>. Filtered <b>writes</b> are still broken by that
/// mechanism (<c>DELETE FROM "T" WHERE T.Col = $1</c>); they fail loudly rather than silently and need a
/// different fix, filed as TASK-216.
/// </para>
///
/// <para>
/// <b>Gated on a live server, deliberately not skipped silently.</b> Set <c>BIRKO_PG_HOST</c> (plus
/// optional <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>, see <see cref="Server"/>) to
/// run these. ⚠ TASK-479: this line named <c>BIRKO_PG_TEST</c> until 2026-09-20 — a fossil of the
/// packed <c>host;db;user;pass</c> convention TASK-042 removed. The code below has read the per-field
/// group all along, so the prose was the only thing wrong, and it was wrong in the direction that
/// sends a reader to export a variable nothing consults. The reproduction that closed the task
/// used EDB's portable binaries — no Docker, no admin, no service:
/// <code>
/// initdb -D data -U birko -A md5 --pwfile=pw.txt -E UTF8
/// pg_ctl -D data -l pg.log -o "-p 55432 -c listen_addresses=127.0.0.1" start
/// </code>
/// </para>
/// </summary>
public class PostgreSqlViewRoundTripTests : IDisposable
{
    // ── live-server gate ──

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

    private readonly List<string> _drop = new();

    public void Dispose()
    {
        if (!Server) return;
        try
        {
            using var conn = new NpgsqlConnection(Settings().GetConnectionString());
            conn.Open();
            foreach (var stmt in _drop)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = stmt;
                try { cmd.ExecuteNonQuery(); } catch { }
            }
        }
        catch { }
    }

    // ── models ──

    public class PgPerson : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class PgOrder : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>PascalCase view properties, which is the normal case and the one that folded.</summary>
    public class PgTotalsView
    {
        public string PersonName { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    private sealed class PgPersonMapping : IModelMapping<PgPerson>
    {
        public void Configure(ModelMap<PgPerson> map)
        {
            map.ToTable("PgPersons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private sealed class PgOrderMapping : IModelMapping<PgOrder>
    {
        public void Configure(ModelMap<PgOrder> map)
        {
            map.ToTable("PgOrders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Amount);
        }
    }

    private static ViewDefinition TotalsDefinition(PortableViewQueryMode mode)
        => new ViewDefinitionBuilder<PgTotalsView>()
            .HasName("PgTotals")
            .HasQueryMode(mode)
            .From<PgOrder>()
            .Join<PgOrder, PgPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<PgPerson, string>(p => p.Name!, v => v.PersonName)
            .GroupBy<PgPerson, string>(p => p.Name!)
            .Count<PgOrder>(v => v.OrderCount)
            .Sum<PgOrder, decimal>(o => o.Amount, v => v.TotalAmount)
            .Build();

    private PostgreSQLConnector Seed()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new PgPersonMapping());
        registry.Register(new PgOrderMapping());
        registry.ApplyToDatabase();

        var connector = new PostgreSQLConnector(Settings());
        _drop.Add("DROP VIEW IF EXISTS \"PgTotals\" CASCADE");
        _drop.Add("DROP TABLE IF EXISTS \"PgOrders\" CASCADE");
        _drop.Add("DROP TABLE IF EXISTS \"PgPersons\" CASCADE");

        Exec("DROP VIEW IF EXISTS \"PgTotals\" CASCADE");
        Exec("DROP TABLE IF EXISTS \"PgOrders\" CASCADE");
        Exec("DROP TABLE IF EXISTS \"PgPersons\" CASCADE");

        connector.CreateTable(new[] { typeof(PgPerson), typeof(PgOrder) });

        foreach (var (name, amount) in new[] { ("a", 10m), ("b", 20m) })
        {
            var person = new PgPerson { Guid = Guid.NewGuid(), Name = name };
            connector.Insert(typeof(PgPerson), person);
            connector.Insert(typeof(PgOrder), new PgOrder
            {
                Guid = Guid.NewGuid(),
                PersonId = person.Guid!.Value,
                Amount = amount,
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

    // ── the reproduction, and the fix ──

    [SkippableFact]
    public void A_persistent_view_is_created_on_postgresql()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        // Asserted by asking the CATALOGUE, not by `create.Should().NotThrow()`.
        //
        // The obvious NotThrow assertion was written first and PASSED against the reverted fix — because
        // this layer swallows the PostgresException (42P01: missing FROM-clause entry for table
        // "avpersons") and reports success. It is the same swallowing that turns the broken on-the-fly path
        // into an empty result rather than an error (TASK-211). A "did not throw" assertion over a
        // swallowing call stack asserts nothing at all, which is exactly how this defect survived: the
        // capability had never worked on PostgreSQL and nothing anywhere went red.
        var connector = Seed();
        var view = SqlViewTranslator.Translate(TotalsDefinition(PortableViewQueryMode.Persistent));

        connector.CreateView(view);

        ViewExistsInCatalogue("PgTotals").Should().BeTrue(
            "the view must actually exist afterwards — CreateView swallows the failure, so only the "
            + "catalogue can distinguish 'created' from 'silently did not create'");
    }

    [SkippableFact]
    public void The_created_columns_are_exactly_what_the_persistent_read_asks_for()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        // The invariant, asserted against the catalogue rather than against a literal. PostgreSQL folds an
        // unquoted alias, so what it stores is lower case; the read emits the same identifier unquoted and
        // therefore folds identically. Comparing case-insensitively is not a weakening — it is the property
        // that has to hold, and asserting exact case here would assert PostgreSQL's folding, not the fix.
        var connector = Seed();
        var definition = TotalsDefinition(PortableViewQueryMode.Persistent);
        var view = SqlViewTranslator.Translate(definition);
        connector.CreateView(view);

        var created = CreatedColumns("PgTotals");

        created.Should().BeEquivalentTo(
            view.GetPersistentViewSelectFields().Values,
            o => o.Using<string>(c => c.Subject.Should().BeEquivalentTo(c.Expectation)).WhenTypeIs<string>());
    }

    [SkippableFact]
    public void A_persistent_view_round_trips_end_to_end_on_postgresql()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        // The capability the defect made unreachable. The read is the sink the task was filed for
        // (`column "Name" does not exist`), reached only once the two in front of it were fixed.
        var connector = Seed();
        var definition = TotalsDefinition(PortableViewQueryMode.Persistent);
        connector.CreateView(SqlViewTranslator.Translate(definition));
        var store = new SqlViewStore<PgTotalsView>(connector, definition);

        var rows = store.QueryAsync(null, OrderBy<PgTotalsView>.By(x => x.PersonName), null, null)
            .GetAwaiter().GetResult().ToList();

        rows.Select(r => r.PersonName).Should().Equal("a", "b");
        rows.Select(r => r.OrderCount).Should().Equal(1, 1);
        rows.Select(r => r.TotalAmount).Should().Equal(10m, 20m);
    }

    private static bool ViewExistsInCatalogue(string viewName)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from information_schema.views where table_name = @t";
        cmd.Parameters.AddWithValue("t", viewName);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static List<string> CreatedColumns(string viewName)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select column_name from information_schema.columns where table_name = @t order by ordinal_position";
        cmd.Parameters.AddWithValue("t", viewName);
        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }
}
