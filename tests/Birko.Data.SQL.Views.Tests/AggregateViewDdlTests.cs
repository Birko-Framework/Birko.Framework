using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using Birko.Models.SQL.Mapping;
using PortableViewQueryMode = Birko.Data.Views.ViewQueryMode;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-129 — an aggregate view's generated DDL carried a DOUBLE alias, so no persistent (or
/// <c>Auto</c>) aggregate view could be created at all.
///
/// <para>
/// <b>The defect, measured.</b> <c>Table.GetSelectFields(withName)</c> appends <c>" as " + &lt;Fields
/// dictionary key&gt;</c> to every aggregate, and both view builders keyed an aggregate by its SQL
/// <i>function</i> name — so the projection read <c>COUNT(AvOrders.PersonId) as COUNT</c>. On top of that,
/// <c>ViewSelectSqlBuilder</c> appended a second <c>AS "OrderCount"</c> (CR-L195, aliasing by view property
/// so two same-function aggregates cannot collide in the DDL). The result was
/// <c>COUNT(AvOrders.PersonId) as COUNT AS "OrderCount"</c> — two aliases on one column, which SQLite
/// rejects with <c>near "AS": syntax error</c> and which is a syntax error on every other provider too.
/// CR-L195's intent was right; it did not notice the inner alias already existed.
/// </para>
///
/// <para>
/// <b>Two further defects with the same root cause, found while reproducing this one.</b>
/// <list type="number">
/// <item><b>A second aggregate of the same function was silently dropped.</b> <c>View.AddField</c> skips a
/// <c>Fields</c> key it already holds, so two <c>Sum</c>s on one table — both keyed <c>"SUM"</c> — produced
/// ONE column. No error, no log entry; the lost property read back as <c>default(T)</c>. That is the very
/// collision CR-L195's alias was written to prevent, one layer down at the dictionary key, and it is
/// <i>worse</i> than the filed defect: a plausible wrong answer rather than a loud syntax error.</item>
/// <item><b>The surviving alias must be unquoted.</b> Keeping CR-L195's <c>AS "OrderCount"</c> would satisfy
/// its shipped test and be latently wrong on PostgreSQL, where a quoted DDL alias creates a case-sensitive
/// column while the read-back (<c>GetPersistentViewSelectFields()</c> → bare <c>OrderCount</c>, emitted
/// unquoted like every column identifier in this codebase) folds to lower case and would not find it.
/// Inert only because the DDL never executed.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>The fix.</b> An aggregate has one public identity — its view property — and three places must agree
/// on it: the SELECT-list alias (which <i>becomes</i> the persistent view's column name),
/// <c>GetPersistentViewSelectFields()</c> (which queries it back) and <c>DataBase.ViewOrderFieldName()</c>
/// (which sorts by it). All three now read <c>field.Property.Name</c>, so they agree by construction rather
/// than by two independent builders keying the field identically. Keying is fixed too, so the collision is
/// gone, but the DDL no longer depends on it.
/// </para>
///
/// <para>
/// Every DDL assertion here <b>executes</b> the statement against a real SQLite file and then reads the
/// resulting columns back out of the database, because a string-level assertion is exactly what let this
/// ship: CR-L195's test asserted <c>Contain("AS \"OrderCount\"")</c>, which passes happily on
/// <c>as COUNT AS "OrderCount"</c>. The alias is never compared to a literal — it is compared to what
/// <c>GetPersistentViewSelectFields()</c> asks for, which is the invariant that actually has to hold.
/// </para>
/// </summary>
public class AggregateViewDdlTests : IDisposable
{
    private readonly string _root;

    public AggregateViewDdlTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-aggddl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // ─────────────────────────────────────────────────────────────────── models & views

    public class AvPerson : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class AvOrder : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
        public decimal Tax { get; set; }
    }

    /// <summary>Two aggregates of DIFFERENT functions — the shape the finding reported.</summary>
    public class AvTotalsView
    {
        public string PersonName { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    /// <summary>
    /// Two aggregates of the SAME function over DIFFERENT source columns. Same function so the dictionary
    /// key collides; different columns so the two values differ — a same-column pair would sum to the same
    /// number and could not tell a dropped column from a duplicated one.
    /// </summary>
    public class AvTwoSumsView
    {
        public string PersonName { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public decimal TotalTax { get; set; }
    }

    private sealed class AvPersonMapping : IModelMapping<AvPerson>
    {
        public void Configure(ModelMap<AvPerson> map)
        {
            map.ToTable("AvPersons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private sealed class AvOrderMapping : IModelMapping<AvOrder>
    {
        public void Configure(ModelMap<AvOrder> map)
        {
            map.ToTable("AvOrders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Amount);
            map.Property(x => x.Tax);
        }
    }

    // ─────────────────────────────────────────────────────────────────── fixture

    private string ConnectionString() => new SqLiteSettings(_root, "aggddl.db").GetConnectionString();

    /// <summary>
    /// Applies the model maps. Every test calls this (directly or via <see cref="Seed"/>) rather than
    /// leaning on whichever sibling happened to run first — <c>ApplyToDatabase</c> is process-global, so a
    /// translator-only test that skipped it would pass or fail on test ordering.
    /// </summary>
    private static void RegisterMappings()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new AvPersonMapping());
        registry.Register(new AvOrderMapping());
        registry.ApplyToDatabase();
    }

    /// <summary>Two people; one order each, with distinct Amount and Tax so every aggregate is observable.</summary>
    private SqLiteConnector Seed()
    {
        RegisterMappings();

        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "aggddl.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(AvPerson), typeof(AvOrder) });

        foreach (var (name, amount, tax) in new[] { ("a", 10m, 1m), ("b", 20m, 2m) })
        {
            var person = new AvPerson { Guid = Guid.NewGuid(), Name = name };
            connector.Insert(typeof(AvPerson), person);
            connector.Insert(typeof(AvOrder), new AvOrder
            {
                Guid = Guid.NewGuid(),
                PersonId = person.Guid!.Value,
                Amount = amount,
                Tax = tax,
            });
        }
        return connector;
    }

    private static ViewDefinition TotalsDefinition(PortableViewQueryMode mode)
        => new ViewDefinitionBuilder<AvTotalsView>()
            .HasName("AvTotals")
            .HasQueryMode(mode)
            .From<AvOrder>()
            .Join<AvOrder, AvPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<AvPerson, string>(p => p.Name!, v => v.PersonName)
            .GroupBy<AvPerson, string>(p => p.Name!)
            .Count<AvOrder>(v => v.OrderCount)
            .Sum<AvOrder, decimal>(o => o.Amount, v => v.TotalAmount)
            .Build();

    private static ViewDefinition TwoSumsDefinition(PortableViewQueryMode mode)
        => new ViewDefinitionBuilder<AvTwoSumsView>()
            .HasName("AvTwoSums")
            .HasQueryMode(mode)
            .From<AvOrder>()
            .Join<AvOrder, AvPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<AvPerson, string>(p => p.Name!, v => v.PersonName)
            .GroupBy<AvPerson, string>(p => p.Name!)
            .Sum<AvOrder, decimal>(o => o.Amount, v => v.TotalAmount)
            .Sum<AvOrder, decimal>(o => o.Tax, v => v.TotalTax)
            .Build();

    /// <summary>The columns SQLite actually created for a view — read back out of the database.</summary>
    private List<string> PhysicalViewColumns(string viewName)
    {
        using var conn = new SqliteConnection(ConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{viewName}\" LIMIT 0";
        using var reader = cmd.ExecuteReader();
        return Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
    }

    // ────────────────────────────────────── the defect: the generated DDL has to execute

    [Fact]
    public void Generated_ddl_for_a_count_and_a_sum_executes()
    {
        // The filed defect, at the sink. Before the fix this threw
        // SqliteException: SQLite Error 1: 'near "AS": syntax error'
        // on `COUNT(AvOrders.PersonId) as COUNT AS "OrderCount"`.
        var connector = Seed();
        var view = SqlViewTranslator.Translate(TotalsDefinition(PortableViewQueryMode.Persistent));

        var create = () => connector.CreateView(view);

        create.Should().NotThrow();
    }

    [Fact]
    public void Generated_ddl_creates_exactly_the_columns_the_persistent_read_asks_for()
    {
        // The invariant CR-L195 was actually about, asserted against the database rather than against a
        // literal: whatever the DDL aliases the aggregate to must be the column
        // GetPersistentViewSelectFields() queries back, or the persistent read fails in the other direction.
        var connector = Seed();
        var view = SqlViewTranslator.Translate(TotalsDefinition(PortableViewQueryMode.Persistent));
        connector.CreateView(view);

        var created = PhysicalViewColumns("AvTotals");

        created.Should().BeEquivalentTo(view.GetPersistentViewSelectFields().Values);
    }

    [Fact]
    public void An_aggregate_projection_carries_one_alias_and_it_is_the_view_property()
    {
        // Positive assertion of the shape, not "does not contain the broken one". The pre-fix projection
        // was `COUNT(AvOrders.PersonId) as COUNT AS "OrderCount"`, so both the trailing-token check and
        // the alias-count check below fail on it.
        RegisterMappings();
        var view = SqlViewTranslator.Translate(TotalsDefinition(PortableViewQueryMode.Persistent));

        var sql = ViewSelectSqlBuilder.BuildViewSelectSql(view, id => "\"" + id + "\"");
        var projection = sql.Substring("SELECT ".Length, sql.IndexOf(" FROM ", StringComparison.Ordinal) - "SELECT ".Length);
        var items = projection.Split(',').Select(x => x.Trim()).ToList();

        items.Should().Contain(i => i.StartsWith("COUNT(", StringComparison.Ordinal) && i.EndsWith(" AS \"OrderCount\"", StringComparison.Ordinal));
        items.Should().Contain(i => i.StartsWith("SUM(", StringComparison.Ordinal) && i.EndsWith(" AS \"TotalAmount\"", StringComparison.Ordinal));
        foreach (var item in items)
        {
            AliasKeywordCount(item).Should().BeLessThanOrEqualTo(1, $"'{item}' must carry at most one alias");
        }
    }

    [Fact]
    public void The_ddl_alias_is_quoted_exactly_as_the_persistent_read_quotes_it()
    {
        // The invariant that decides quoted-vs-bare, and the one an earlier draft of this fix got backwards.
        //
        // This alias *creates* an identifier rather than referencing one, and its only reader is
        // CreatePersistentViewSelectCommand, which emits `QuoteIdentifier(GetPersistentViewSelectFields()[i])`
        // through the SAME connector. So the DDL must quote it the way that reader quotes it, or the view is
        // created and then cannot be queried: on PostgreSQL a bare `as OrderCount` creates `ordercount` while
        // the reader asks for `"OrderCount"`.
        //
        // SQLite is case-insensitive for identifiers, so an end-to-end SQLite test passes either way — which
        // is exactly why this is asserted structurally, by generating both halves with one quoter and
        // checking they agree, instead of trusting a green round-trip.
        // Scoped to AGGREGATE columns, which is what this task owns. The non-aggregate columns have the
        // same mismatch in the opposite direction and it is PRE-EXISTING: the DDL projects them as an
        // unquoted `AvPersons.Name` (so PostgreSQL names the view column `name`) while the persistent read
        // asks for `"Name"`. Asserting the whole column set here failed on exactly that, which is how it
        // was found — filed as TASK-209, deliberately not fixed under this task, and not asserted here,
        // because a test that encodes it either way would bless one side of an open question.
        RegisterMappings();
        var view = SqlViewTranslator.Translate(TotalsDefinition(PortableViewQueryMode.Persistent));
        Func<string, string> quote = id => "\"" + id.Replace("\"", "\"\"") + "\"";

        var ddl = ViewSelectSqlBuilder.BuildViewSelectSql(view, quote);

        var aggregateColumns = view.GetTableFields()
            .Where(f => f.IsAggregate && f.Property != null)
            .Select(f => f.Property.Name)
            .ToList();
        aggregateColumns.Should().NotBeEmpty("the fixture has a Count and a Sum");
        foreach (var column in aggregateColumns)
        {
            view.GetPersistentViewSelectFields().Values.Should().Contain(column);
            ddl.Should().Contain(quote(column),
                $"the persistent read emits {quote(column)}, so the DDL must create the column under exactly that spelling");
        }
    }

    // ────────────────────────────────── the silent half: same-function aggregates

    [Fact]
    public void Two_aggregates_of_the_same_function_both_reach_the_generated_ddl()
    {
        // Before the fix both Sums were keyed "SUM", View.AddField kept the first and dropped the second,
        // and the emitted projection listed ONE aggregate. No exception, no log entry.
        RegisterMappings();
        var view = SqlViewTranslator.Translate(TwoSumsDefinition(PortableViewQueryMode.Persistent));

        var sql = ViewSelectSqlBuilder.BuildViewSelectSql(view, id => "\"" + id + "\"");

        sql.Should().Contain("AS \"TotalAmount\"");
        sql.Should().Contain("AS \"TotalTax\"");
        view.GetPersistentViewSelectFields().Values.Should().Contain(new[] { "TotalAmount", "TotalTax" });
    }

    [Fact]
    public void The_on_the_fly_projection_aliases_an_aggregate_by_its_view_property()
    {
        // The other half of the alias fix, on the path that does NOT go through the DDL builder.
        // `Table.GetSelectFields` used to alias from the Fields dictionary key — the SQL function name —
        // so two same-function aggregates both projected `as SUM`. It now reads the field's own
        // `Property.Name`, which is the single identity the DDL alias and the persistent read also use.
        // Unquoted here, correctly: this alias names a column in a result set nothing looks up by name,
        // and it matches how every other column identifier on this path is emitted.
        RegisterMappings();
        var view = SqlViewTranslator.Translate(TwoSumsDefinition(PortableViewQueryMode.OnTheFly));

        var projected = view.GetSelectFields().Values.ToList();

        projected.Should().Contain(v => v.EndsWith(" as TotalAmount", StringComparison.Ordinal));
        projected.Should().Contain(v => v.EndsWith(" as TotalTax", StringComparison.Ordinal));
        projected.Should().NotContain(v => v.EndsWith(" as SUM", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_aggregates_of_the_same_function_round_trip_to_their_own_values()
    {
        // The assertion that distinguishes "both columns exist" from "both columns are correct". Amount and
        // Tax differ per row, so a dropped-then-defaulted column reads 0 and a duplicated one reads the
        // other aggregate's value; only correct wiring gives 10/1 and 20/2.
        var connector = Seed();
        var definition = TwoSumsDefinition(PortableViewQueryMode.Persistent);
        connector.CreateView(SqlViewTranslator.Translate(definition));
        var store = new SqlViewStore<AvTwoSumsView>(connector, definition);

        var rows = store.QueryAsync(null, OrderBy<AvTwoSumsView>.By(x => x.PersonName), null, null)
            .GetAwaiter().GetResult().ToList();

        rows.Select(r => r.PersonName).Should().Equal("a", "b");
        rows.Select(r => r.TotalAmount).Should().Equal(10m, 20m);
        rows.Select(r => r.TotalTax).Should().Equal(1m, 2m);
    }

    // ────────────────────────────────── end-to-end, and the path that must not change

    [Fact]
    public void A_persistent_aggregate_view_round_trips_end_to_end()
    {
        // The capability the defect made unreachable: create the view from the generator, then read it
        // back through the store's persistent branch.
        var connector = Seed();
        var definition = TotalsDefinition(PortableViewQueryMode.Persistent);
        connector.CreateView(SqlViewTranslator.Translate(definition));
        var store = new SqlViewStore<AvTotalsView>(connector, definition);

        var rows = store.QueryAsync(null, OrderBy<AvTotalsView>.By(x => x.PersonName), null, null)
            .GetAwaiter().GetResult().ToList();

        rows.Select(r => r.PersonName).Should().Equal("a", "b");
        rows.Select(r => r.OrderCount).Should().Equal(1, 1);
        rows.Select(r => r.TotalAmount).Should().Equal(10m, 20m);
    }

    [Fact]
    public void The_on_the_fly_aggregate_path_still_materialises_rows()
    {
        // Contract pin. The fix changes the shared Table.GetSelectFields alias text (`as COUNT` →
        // `as OrderCount`) which the on-the-fly SELECT list also carries, so this asserts the change did
        // not disturb the path that was already working. It reads positionally, so it should not care —
        // that is the claim being checked, not assumed.
        var connector = Seed();
        var store = new SqlViewStore<AvTotalsView>(connector, TotalsDefinition(PortableViewQueryMode.OnTheFly));

        var rows = store.QueryAsync(null, OrderBy<AvTotalsView>.By(x => x.PersonName), null, null)
            .GetAwaiter().GetResult().ToList();

        rows.Select(r => r.PersonName).Should().Equal("a", "b");
        rows.Select(r => r.OrderCount).Should().Equal(1, 1);
        rows.Select(r => r.TotalAmount).Should().Equal(10m, 20m);
    }

    // ────────────────────────────────── the attribute-driven builder's identical defect

    [Table("AvaPersons")]
    public class AvaPerson : AbstractModel
    {
        public string? Name { get; set; }
    }

    [Table("AvaOrders")]
    public class AvaOrder : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
        public decimal Tax { get; set; }
    }

    [View(typeof(AvaPerson), typeof(AvaOrder), nameof(AvaPerson.Guid), nameof(AvaOrder.PersonId), name: "AvaTwoSums")]
    public class AvaTwoSumsView
    {
        [ViewField(typeof(AvaPerson), nameof(AvaPerson.Name))]
        public string PersonName { get; set; } = string.Empty;

        [SumField(typeof(AvaOrder), nameof(AvaOrder.Amount))]
        public decimal TotalAmount { get; set; }

        [SumField(typeof(AvaOrder), nameof(AvaOrder.Tax))]
        public decimal TotalTax { get; set; }
    }

    [Fact]
    public void The_attribute_builder_also_keeps_both_same_function_aggregates()
    {
        // DataBase.LoadView is the other builder and had the identical collision — it passed
        // tableField.Name, which after the FunctionField reassignment is the SQL function name. It also
        // carried a dead `tableFieldName` local concatenating table+function name, an abandoned attempt at
        // exactly this uniqueness. Both builders are covered because a fix to one would not fix the other.
        RegisterMappings();
        var view = DataBase.LoadView(typeof(AvaTwoSumsView));

        view.Should().NotBeNull();
        var sql = ViewSelectSqlBuilder.BuildViewSelectSql(view!, id => "\"" + id + "\"");

        sql.Should().Contain("AS \"TotalAmount\"");
        sql.Should().Contain("AS \"TotalTax\"");
        sql.Should().NotContain(" as SUM");
        view!.GetPersistentViewSelectFields().Values.Should().Contain(new[] { "TotalAmount", "TotalTax" });
    }

    /// <summary>
    /// Counts alias keywords in one projection item, ignoring anything inside the aggregate's parentheses
    /// so a column named e.g. <c>Cast</c> cannot be miscounted.
    /// </summary>
    private static int AliasKeywordCount(string projectionItem)
    {
        var depth = 0;
        var count = 0;
        var tokens = projectionItem.Split(' ');
        foreach (var token in tokens)
        {
            depth += token.Count(c => c == '(') - token.Count(c => c == ')');
            if (depth == 0 && token.Equals("as", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }
        return count;
    }
}
