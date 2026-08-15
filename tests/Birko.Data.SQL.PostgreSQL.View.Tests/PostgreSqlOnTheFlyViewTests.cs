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
/// TASK-211 — the ON-THE-FLY half of the identifier defect TASK-209 closed for the persistent path, plus
/// the swallow that hid both.
///
/// <para>
/// Gated on a live server exactly as <see cref="PostgreSqlViewRoundTripTests"/> is: set
/// <c>BIRKO_PG_HOST</c> (+ <c>BIRKO_PG_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>).
/// </para>
/// </summary>
public class PostgreSqlOnTheFlyViewTests : IDisposable
{
    // ── live-server gate (mirrors PostgreSqlViewRoundTripTests) ──

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";

    private static bool Server => !string.IsNullOrWhiteSpace(Host);

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    public void Dispose()
    {
        if (!Server) return;
        try
        {
            Exec("DROP VIEW IF EXISTS \"OfTotals\" CASCADE");
            Exec("DROP TABLE IF EXISTS \"OfOrders\" CASCADE");
            Exec("DROP TABLE IF EXISTS \"OfPersons\" CASCADE");
        }
        catch { }
    }

    // ── models (PascalCase, which is the normal case and the one that folds) ──

    public class OfPerson : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class OfOrder : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
    }

    public class OfTotalsView
    {
        public string PersonName { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    private sealed class OfPersonMapping : IModelMapping<OfPerson>
    {
        public void Configure(ModelMap<OfPerson> map)
        {
            map.ToTable("OfPersons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private sealed class OfOrderMapping : IModelMapping<OfOrder>
    {
        public void Configure(ModelMap<OfOrder> map)
        {
            map.ToTable("OfOrders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Amount);
        }
    }

    private static ViewDefinition TotalsDefinition()
        => new ViewDefinitionBuilder<OfTotalsView>()
            .HasName("OfTotals")
            .HasQueryMode(PortableViewQueryMode.OnTheFly)
            .From<OfOrder>()
            .Join<OfOrder, OfPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<OfPerson, string>(p => p.Name!, v => v.PersonName)
            .GroupBy<OfPerson, string>(p => p.Name!)
            .Count<OfOrder>(v => v.OrderCount)
            .Sum<OfOrder, decimal>(o => o.Amount, v => v.TotalAmount)
            .Build();

    private static PostgreSQLConnector Seed()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new OfPersonMapping());
        registry.Register(new OfOrderMapping());
        registry.ApplyToDatabase();

        var connector = new PostgreSQLConnector(Settings());

        Exec("DROP VIEW IF EXISTS \"OfTotals\" CASCADE");
        Exec("DROP TABLE IF EXISTS \"OfOrders\" CASCADE");
        Exec("DROP TABLE IF EXISTS \"OfPersons\" CASCADE");

        connector.CreateTable(new[] { typeof(OfPerson), typeof(OfOrder) });

        foreach (var (name, amount) in new[] { ("a", 10m), ("b", 20m) })
        {
            var person = new OfPerson { Guid = Guid.NewGuid(), Name = name };
            connector.Insert(typeof(OfPerson), person);
            connector.Insert(typeof(OfOrder), new OfOrder
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

    // ── 1. the capability: an on-the-fly view over PascalCase models ──

    [SkippableFact]
    public void An_on_the_fly_view_round_trips_on_postgresql()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        var connector = Seed();
        var definition = TotalsDefinition();
        var store = new SqlViewStore<OfTotalsView>(connector, definition);

        var rows = store.QueryAsync(null, OrderBy<OfTotalsView>.By(x => x.PersonName), null, null)
            .GetAwaiter().GetResult().ToList();

        rows.Select(r => r.PersonName).Should().Equal("a", "b");
        rows.Select(r => r.OrderCount).Should().Equal(1, 1);
        rows.Select(r => r.TotalAmount).Should().Equal(10m, 20m);
    }

    // ── 2. the swallow: a rejected statement must not read as "no rows" ──

    [SkippableFact]
    public void A_select_the_server_rejects_throws_instead_of_returning_an_empty_result()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        var connector = Seed();

        // The table exists; the COLUMN does not (42703). Nothing about this is "the relation is missing",
        // which is the only case the reader is entitled to answer with an empty sequence.
        var read = () => connector.Select(
                new[] { "OfPersons" },
                new Dictionary<int, string> { { 0, "NoSuchColumn" } },
                reader => new object[] { reader.GetValue(0) })
            .ToList();

        read.Should().Throw<Exception>(
            "a statement the server rejected is a failure, not an empty result — returning zero rows is "
            + "indistinguishable from a table that legitimately has none");
    }

    [SkippableFact]
    public void A_genuinely_missing_table_still_reads_as_an_empty_result()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        Seed();
        var connector = new PostgreSQLConnector(Settings());

        // The opt-out the swallow exists for (CR-M149 / lazy create-on-first-use): an absent relation is a
        // legitimate empty read, and narrowing the swallow must not close this door.
        var read = () => connector.Select(
                new[] { "NoSuchTableAtAll" },
                new Dictionary<int, string> { { 0, "Guid" } },
                reader => new object[] { reader.GetValue(0) })
            .ToList();

        read.Should().NotThrow();
        read().Should().BeEmpty();
    }

    // ── 3. the plain SELECT paths, which use the same qualifier emitter ──

    [SkippableFact]
    public void A_plain_single_table_select_returns_rows_on_postgresql()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        var connector = Seed();

        // The explicit null cast works around the overload ambiguity filed as TASK-138 (CS0121 between the
        // expression- and condition-keyed overloads) — not this task's defect.
        var people = connector.Select(typeof(OfPerson), (IEnumerable<Conditions.Condition>?)null)
            .Cast<OfPerson>().ToList();

        people.Select(p => p.Name).OrderBy(x => x).Should().Equal("a", "b");
    }

    [SkippableFact]
    public void A_filtered_single_table_select_returns_rows_on_postgresql()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        var connector = Seed();

        System.Linq.Expressions.Expression<Func<OfPerson, bool>> filter = p => p.Name == "a";
        var people = connector.Select(typeof(OfPerson), filter).Cast<OfPerson>().ToList();

        people.Select(p => p.Name).Should().Equal("a");
    }

    // NOTE — filtered WRITES are broken on PostgreSQL by the identical qualifier mechanism and are NOT
    // covered here. Measured 2026-08-15 while closing this task:
    //     DELETE FROM "OfPersons" WHERE OfPersons.Name = $1
    //     ERROR: missing FROM-clause entry for table "ofpersons"
    // They are a different builder and need a different fix — the alias this task uses is not portable to
    // DELETE/UPDATE (MSSql rejects `DELETE FROM t AS a`) — and unlike reads they fail LOUDLY, so they are
    // not the silent-wrong-answer class. Filed as TASK-216 rather than asserted here: a test pinning the
    // broken behaviour would bless it.
    [SkippableFact]
    public void A_plain_multi_table_select_returns_rows_on_postgresql()
    {
        Skip.IfNot(Server, "Set BIRKO_PG_HOST to run against a live PostgreSQL.");

        var connector = Seed();

        var sets = connector
            .Select(new[] { typeof(OfPerson), typeof(OfOrder) }, (IEnumerable<Conditions.Condition>?)null)
            .ToList();

        sets.Should().NotBeEmpty();
    }
}
