using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
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
/// TASK-128 — the view twin of TASK-110 (SH-H003), end-to-end against a real SQLite file.
///
/// Both view ORDER BY emit sites interpolated their keys into <c>CommandText</c> verbatim
/// (<c>AbstractConnectorBase_View.CreatePersistentViewSelectCommand</c> and, via
/// <c>CreateSelectCommand(DbCommand, Tables.View, …)</c>, the entity builder), and nothing between
/// <c>SqlViewStore</c> and those sites resolved anything — <c>TranslateOrderBy</c> just copied
/// <c>OrderByField.PropertyName</c> across. TASK-110's fix resolves at the <c>Tables.Table</c> funnel, which
/// no view read passes through, so the view path kept the original behaviour. Two consequences, both measured
/// here before the fix:
///
/// <list type="bullet">
/// <item><b>Injection, on BOTH paths.</b> <c>ByName("Name; CREATE TABLE Pwned (x INTEGER); --")</c> created
/// that table on the on-the-fly path and on the persistent path alike, neither raising. The " ASC" the
/// builders append is defeated by the trailing comment.</item>
/// <item><b>The type-safe API did not work at all.</b> <c>OrderBy&lt;TView&gt;.By(x =&gt; x.PersonName)</c>
/// emitted the VIEW property name while the columns carry SOURCE names, raising
/// <c>no such column: PersonName</c>. Worse than the entity twin, where only a <c>[NamedField]</c>-remapped
/// column was affected: renaming is the entire point of a view, so sorting a view by one of its own
/// properties never worked. Only <c>ByName("&lt;source column&gt;")</c> did — the same overload that is the
/// injection sink.</item>
/// </list>
///
/// Resolution happens once, in <c>Select(Tables.View, …)</c> / <c>SelectAsync(…)</c>, after
/// <c>usePersistent</c> is known — because the two paths expose their columns under different names, and each
/// key must resolve to what that path's own SELECT list emits: <c>Table.Column</c> on the on-the-fly join
/// select, the bare source column on a persistent view, and for an aggregate the <c>AS &lt;ViewProperty&gt;</c>
/// alias its DDL creates. Nothing is quoted (see TASK-110: quoting would break mixed-case columns on
/// PostgreSQL).
/// </summary>
public class ViewOrderByResolutionTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _executed = new();

    public ViewOrderByResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-vorder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class VPerson : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class VOrder : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
    }

    public class VOrderView
    {
        public Guid? OrderId { get; set; }
        public string PersonName { get; set; } = string.Empty;
    }

    public class VTotalsView
    {
        public string PersonName { get; set; } = string.Empty;
        public int OrderCount { get; set; }
    }

    private sealed class VPersonMapping : IModelMapping<VPerson>
    {
        public void Configure(ModelMap<VPerson> map)
        {
            map.ToTable("VPersons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private sealed class VOrderMapping : IModelMapping<VOrder>
    {
        public void Configure(ModelMap<VOrder> map)
        {
            map.ToTable("VOrders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Amount);
        }
    }

    private string ConnectionString() => new SqLiteSettings(_root, "vorder.db").GetConnectionString();

    /// <summary>Three people, one order each, seeded out of alphabetical order so a sort is observable.</summary>
    private SqLiteConnector Seed()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new VPersonMapping());
        registry.Register(new VOrderMapping());
        registry.ApplyToDatabase();

        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "vorder.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(VPerson), typeof(VOrder) });
        connector.OnExecute += text => _executed.Add(text);

        foreach (var (name, amount) in new[] { ("c", 30m), ("a", 10m), ("b", 20m) })
        {
            var person = new VPerson { Guid = Guid.NewGuid(), Name = name };
            connector.Insert(typeof(VPerson), person);
            connector.Insert(typeof(VOrder), new VOrder { Guid = Guid.NewGuid(), PersonId = person.Guid!.Value, Amount = amount });
        }
        return connector;
    }

    private static ViewDefinition OnTheFlyDefinition()
        => new ViewDefinitionBuilder<VOrderView>()
            .From<VOrder>()
            .Join<VOrder, VPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<VOrder, Guid?>(o => o.Guid, v => v.OrderId)
            .Select<VPerson, string>(p => p.Name!, v => v.PersonName)
            .Build();

    private static ViewDefinition PersistentDefinition()
        => new ViewDefinitionBuilder<VOrderView>()
            .HasName("VOrderPersistent")
            .HasQueryMode(PortableViewQueryMode.Persistent)
            .From<VOrder>()
            .Join<VOrder, VPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<VOrder, Guid?>(o => o.Guid, v => v.OrderId)
            .Select<VPerson, string>(p => p.Name!, v => v.PersonName)
            .Build();

    private static ViewDefinition AggregateDefinition(PortableViewQueryMode mode)
        => new ViewDefinitionBuilder<VTotalsView>()
            .HasName("VTotals")
            .HasQueryMode(mode)
            .From<VOrder>()
            .Join<VOrder, VPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<VPerson, string>(p => p.Name!, v => v.PersonName)
            .GroupBy<VPerson, string>(p => p.Name!)
            .Count<VOrder>(v => v.OrderCount)
            .Build();

    /// <summary>Creates the physical VIEW so the persistent branch has something to query.</summary>
    private void CreatePhysicalView(ViewDefinition definition)
        => ExecuteDdl("CREATE VIEW IF NOT EXISTS \"" + SqlViewTranslator.Translate(definition).Name + "\" AS "
            + ViewSelectSqlBuilder.BuildViewSelectSql(SqlViewTranslator.Translate(definition), id => "\"" + id + "\""));

    private void ExecuteDdl(string sql)
    {
        using var conn = new SqliteConnection(ConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private bool TableExists(string name)
    {
        using var conn = new SqliteConnection(ConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", name);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private List<VOrderView> Query(SqlViewStore<VOrderView> store, OrderBy<VOrderView>? orderBy)
        => store.QueryAsync(null, orderBy, null, null).GetAwaiter().GetResult().ToList();

    // ------------------------------------------------ the type-safe API, on both paths

    [Fact]
    public void OnTheFly_sorting_by_a_view_property_returns_ordered_rows()
    {
        // Before the fix: SqliteException "no such column: PersonName" — the view property was emitted while
        // the joined column is VPersons.Name.
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        var rows = Query(store, OrderBy<VOrderView>.By(x => x.PersonName));

        rows.Should().HaveCount(3);
        rows.Select(r => r.PersonName).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void OnTheFly_sorting_by_a_view_property_descending_reverses_it()
    {
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        var rows = Query(store, OrderBy<VOrderView>.ByDescending(x => x.PersonName));

        rows.Select(r => r.PersonName).Should().Equal("c", "b", "a");
    }

    [Fact]
    public void OnTheFly_emits_the_table_qualified_source_column()
    {
        // The on-the-fly SELECT list is always Table.Column (View.GetSelectFields), so the sort key has to be
        // too — bare "Name" would be ambiguous the moment two joined tables both had that column.
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        Query(store, OrderBy<VOrderView>.By(x => x.PersonName));

        _executed.Should().ContainSingle(t => t.Contains("ORDER BY VPersons.Name ASC"));
        _executed.Should().NotContain(t => t.Contains("ORDER BY PersonName"));
    }

    [Fact]
    public void Persistent_sorting_by_a_view_property_returns_ordered_rows()
    {
        var connector = Seed();
        var definition = PersistentDefinition();
        CreatePhysicalView(definition);
        var store = new SqlViewStore<VOrderView>(connector, definition);

        var rows = Query(store, OrderBy<VOrderView>.By(x => x.PersonName));

        rows.Should().HaveCount(3);
        rows.Select(r => r.PersonName).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Persistent_emits_the_bare_source_column_not_the_qualified_one()
    {
        // A persistent view's columns are the names its DDL projected — a Table.Column prefix would name a
        // table that is not in the FROM clause at all. TASK-209 changed WHICH name that is: the DDL now
        // aliases every column `AS <ViewProperty>`, not just aggregates, so the sort key is the view
        // property (PersonName) rather than the source column (Name). Emitted bare, which is what the rest
        // of that task made the SELECT list and the DDL alias do too.
        var connector = Seed();
        var definition = PersistentDefinition();
        CreatePhysicalView(definition);
        var store = new SqlViewStore<VOrderView>(connector, definition);

        Query(store, OrderBy<VOrderView>.By(x => x.PersonName));

        _executed.Should().ContainSingle(t => t.Contains("FROM \"VOrderPersistent\"") && t.Contains("ORDER BY PersonName ASC"));
        _executed.Should().NotContain(t => t.Contains("ORDER BY VPersons.Name"));
    }

    [Fact]
    public async Task Async_path_sorts_by_a_view_property_too()
    {
        // SqlViewStore prefers SelectAsync on an AbstractAsyncConnector, a separate funnel needing its own
        // guard — the sync fix does not cover it.
        var connector = Seed();
        var store = new SqlViewStore<VOrderView>(connector, OnTheFlyDefinition());

        var rows = (await store.QueryAsync(null, OrderBy<VOrderView>.By(x => x.PersonName), null, null)).ToList();

        rows.Select(r => r.PersonName).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Multi_key_view_sort_applies_the_keys_in_order()
    {
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        var rows = Query(store, OrderBy<VOrderView>.By(x => x.PersonName).ThenByDescending(x => x.OrderId));

        rows.Select(r => r.PersonName).Should().Equal("a", "b", "c");
        _executed.Should().ContainSingle(t => t.Contains("ORDER BY VPersons.Name ASC, VOrders.Guid DESC"));
    }

    // ------------------------------------------------------------------ aggregates

    [Fact]
    public void OnTheFly_aggregate_sort_uses_the_aggregate_expression()
    {
        // The on-the-fly SELECT list emits COUNT(...) AS COUNT, and sorting by the expression is valid in a
        // grouped query on every provider — so the key resolves to what this path projects.
        var connector = Seed();
        var store = new SqlViewStore<VTotalsView>(connector, AggregateDefinition(PortableViewQueryMode.OnTheFly));

        var rows = store.QueryAsync(null, OrderBy<VTotalsView>.By(x => x.OrderCount), null, null)
            .GetAwaiter().GetResult().ToList();

        rows.Should().HaveCount(3);
        _executed.Should().ContainSingle(t => t.Contains("ORDER BY COUNT(") && t.Contains(") ASC"));
    }

    [Fact]
    public void Persistent_aggregate_sort_uses_the_view_property_alias()
    {
        // The persistent path is the one place an aggregate is NOT named by its function: the view DDL
        // aliases it by <ViewProperty> (CR-L195, so two aggregates of one function cannot collide), and
        // GetPersistentViewSelectFields queries it back under that name. The sort key must agree.
        //
        // The DDL is generated (TASK-129). It used to be hand-written here because the generator emitted a
        // DOUBLE alias — `COUNT(VOrders.PersonId) as COUNT AS "OrderCount"`, a syntax error on every
        // provider — so no persistent aggregate view could be created at all. Now that it can, the hand-
        // written SQL is gone: it was a guess at what the generator intended, and a guess is exactly what
        // this test must not depend on.
        var connector = Seed();
        var definition = AggregateDefinition(PortableViewQueryMode.Persistent);
        CreatePhysicalView(definition);
        var store = new SqlViewStore<VTotalsView>(connector, definition);

        var rows = store.QueryAsync(null, OrderBy<VTotalsView>.By(x => x.OrderCount), null, null)
            .GetAwaiter().GetResult().ToList();

        rows.Should().HaveCount(3);
        _executed.Should().ContainSingle(t => t.Contains("ORDER BY OrderCount ASC"));
    }

    [Fact]
    public void Persistent_aggregate_sort_by_the_sql_function_name_resolves_to_the_alias()
    {
        // The case that actually distinguishes resolved from unresolved on this path. A view's aggregate
        // FunctionField is named after its SQL function, so `ByName("COUNT")` names a real field — but the
        // persistent view has no COUNT column, it has the aliased "OrderCount". Unresolved this emitted
        // `ORDER BY COUNT` and the database rejected it; resolved it becomes the alias.
        //
        // Its sibling above happens to pass either way, because for an aggregate the view property name and
        // the resolved persistent column name coincide — so that one is a contract pin, and this is the proof.
        //
        // DDL generated rather than hand-written since TASK-129 — see the sibling above.
        var connector = Seed();
        var definition = AggregateDefinition(PortableViewQueryMode.Persistent);
        CreatePhysicalView(definition);
        var store = new SqlViewStore<VTotalsView>(connector, definition);

        var rows = store.QueryAsync(null, OrderBy<VTotalsView>.ByName("COUNT"), null, null)
            .GetAwaiter().GetResult().ToList();

        rows.Should().HaveCount(3);
        _executed.Should().ContainSingle(t => t.Contains("ORDER BY OrderCount ASC"));
    }

    // ------------------------------------------------------------------ back-compat

    [Fact]
    public void Sorting_by_the_source_column_name_still_returns_the_same_rows()
    {
        // ByName("<source column>") is the only view sort that worked before the fix, so it has to keep
        // working. On the on-the-fly path its emitted identifier now carries the table prefix the SELECT list
        // already used — same column, and unambiguous where bare was not.
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        var rows = Query(store, OrderBy<VOrderView>.ByName("Name"));

        rows.Select(r => r.PersonName).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Persistent_sorting_by_the_source_column_name_is_unchanged()
    {
        var connector = Seed();
        var definition = PersistentDefinition();
        CreatePhysicalView(definition);
        var store = new SqlViewStore<VOrderView>(connector, definition);

        Query(store, OrderBy<VOrderView>.ByName("Name")).Select(r => r.PersonName).Should().Equal("a", "b", "c");

        // Sorting by the SOURCE column name still resolves — ResolveViewOrderFields matches a key against
        // Property.Name or Name, so "Name" still finds the field. TASK-209 changed only what is then
        // EMITTED: the persistent view's column is now the view property, so the clause reads PersonName.
        // The rows are the assertion that matters and they are unchanged.
        _executed.Should().ContainSingle(t => t.EndsWith("ORDER BY PersonName ASC"));
    }

    [Fact]
    public void No_order_by_emits_no_ORDER_BY_clause()
    {
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        Query(store, null).Should().HaveCount(3);

        _executed.Should().NotContain(t => t.Contains("ORDER BY"));
    }

    // ------------------------------------------------------------------- injection

    [Fact]
    public void OnTheFly_batch_separator_payload_cannot_create_a_table()
    {
        // Measured pre-fix: this created Pwned, silently.
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        var act = () => Query(store, OrderBy<VOrderView>.ByName("Name; CREATE TABLE Pwned (x INTEGER); --"));

        act.Should().Throw<ArgumentException>();
        TableExists("Pwned").Should().BeFalse("the injected statement must never reach the database");
    }

    [Fact]
    public void Persistent_batch_separator_payload_cannot_create_a_table()
    {
        // Measured pre-fix: this created Pwned2 — the persistent builder is a second, independent emit site,
        // so it needs its own proof.
        var connector = Seed();
        var definition = PersistentDefinition();
        CreatePhysicalView(definition);
        var store = new SqlViewStore<VOrderView>(connector, definition);

        var act = () => Query(store, OrderBy<VOrderView>.ByName("Name; CREATE TABLE Pwned2 (x INTEGER); --"));

        act.Should().Throw<ArgumentException>();
        TableExists("Pwned2").Should().BeFalse();
    }

    [Fact]
    public void A_comment_payload_cannot_override_the_callers_limit()
    {
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        var act = () => store.QueryAsync(null, OrderBy<VOrderView>.ByName("Name LIMIT 1 --"), 100, null)
            .GetAwaiter().GetResult().ToList();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_subquery_payload_is_rejected()
    {
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        var act = () => Query(store, OrderBy<VOrderView>.ByName("(SELECT count(*) FROM sqlite_master)"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_async_path_rejects_the_same_payload()
    {
        var connector = Seed();
        var store = new SqlViewStore<VOrderView>(connector, OnTheFlyDefinition());

        var act = async () => await store.QueryAsync(
            null, OrderBy<VOrderView>.ByName("Name; CREATE TABLE Pwned3 (x INTEGER); --"), null, null);

        await act.Should().ThrowAsync<ArgumentException>();
        TableExists("Pwned3").Should().BeFalse();
    }

    [Fact]
    public void An_unknown_sort_key_names_the_key_and_the_view()
    {
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        var act = () => Query(store, OrderBy<VOrderView>.ByName("NoSuchThing"));

        // Not a SqliteException: the framework must name what it could not resolve, rather than letting the
        // provider report a column the developer never wrote.
        act.Should().Throw<ArgumentException>()
            .WithMessage("*NoSuchThing*")
            .WithMessage("*VOrders*");
        act.Should().NotThrow<SqliteException>();
    }

    [Fact]
    public void A_column_of_a_source_table_that_the_view_does_not_project_is_rejected()
    {
        // Resolution is scoped to what the VIEW exposes, not to every column its source tables have.
        // VOrder.Amount is joined but never selected, so it is not a legal sort key.
        var store = new SqlViewStore<VOrderView>(Seed(), OnTheFlyDefinition());

        var act = () => Query(store, OrderBy<VOrderView>.ByName("Amount"));

        act.Should().Throw<ArgumentException>();
    }
}
