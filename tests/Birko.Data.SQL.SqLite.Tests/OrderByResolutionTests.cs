using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// SH-H003 / SH-M022 (TASK-110), end-to-end against a real SQLite file.
///
/// The ORDER BY clause interpolated its keys into <c>CommandText</c> verbatim
/// (<c>AbstractConnectorBase.CreateSelectCommand</c>), and the store hands it whatever
/// <c>OrderBy&lt;T&gt;</c> holds — including the arbitrary string <c>ByName</c> accepts. Two consequences,
/// one call site, both measured here before the fix:
///
/// <list type="bullet">
/// <item><b>SH-H003, injection.</b> <c>ByName("Rank; CREATE TABLE Pwned (x INTEGER); --")</c> CREATED the
/// table; <c>ByName("Rank LIMIT 1 --")</c> commented out the framework's own <c>LIMIT</c> and returned 1
/// row of 3. Neither raised anything. The <c>--</c> defeats the " ASC" the builder appends, so the suffix
/// is not a mitigation.</item>
/// <item><b>SH-M022, remapped columns unsortable.</b> Keys are CLR property names, so a
/// <c>[NamedField("label_col")]</c> property emitted <c>ORDER BY Label</c> and SQLite answered
/// <c>no such column: Label</c>. Note this contradicts how the finding was filed ("returns empty instead
/// of throwing") — <c>IsMissingTableException</c> matches only "no such table", so the provider exception
/// propagated out of the reader iterator.</item>
/// </list>
///
/// A key that reaches the clause is now a name resolved out of table metadata
/// (<c>DataBase.ResolveOrderFields</c>) — the resolution is the whitelist. Nothing is quoted: this codebase
/// emits column identifiers bare everywhere (DDL included), and quoting only here would break mixed-case
/// columns on PostgreSQL, where an unquoted DDL identifier is folded to lower case.
///
/// These cases run against a real database on purpose. An emitted-SQL assertion alone would have accepted
/// the pre-fix clause as "valid text", and the whole finding is about what the database then does with it.
/// </summary>
public class OrderByResolutionTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _executed = new();

    public OrderByResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-orderby-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("ORows")]
    public class ORow : AbstractModel
    {
        [NamedField("label_col")]
        public string? Label { get; set; }

        public int Rank { get; set; }
    }

    private string DbPath => Path.Combine(_root, "orderby.db");

    private void CreateSchema()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "orderby.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(ORow) });
    }

    private SqLiteConnector Connector()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "orderby.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.OnExecute += text => _executed.Add(text);
        return connector;
    }

    private SQLiteStore<ORow> NewStore()
    {
        CreateSchema();
        var store = new SQLiteStore<ORow>();
        store.SetSettings(new SqLiteSettings(_root, "orderby.db"));
        return store;
    }

    private AsyncSQLiteStore<ORow> NewAsyncStore()
    {
        CreateSchema();
        var store = new AsyncSQLiteStore<ORow>();
        store.SetSettings(new SqLiteSettings(_root, "orderby.db"));
        return store;
    }

    private static List<ORow> Seed() => new()
    {
        new ORow { Label = "c", Rank = 3 },
        new ORow { Label = "a", Rank = 1 },
        new ORow { Label = "b", Rank = 2 },
    };

    private bool TableExists(string name)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", name);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    // ------------------------------------------------------------------ SH-M022

    [Fact]
    public void Ordering_by_a_remapped_property_returns_ordered_rows()
    {
        // Before the fix this threw SqliteException "no such column: Label" on enumeration: the CLR property
        // name was emitted, and the table only has label_col.
        var store = NewStore();
        store.Create(Seed());

        var rows = store.Read(null, OrderBy<ORow>.By(x => x.Label), null, null).ToList();

        rows.Should().HaveCount(3, "a remapped column must be sortable at all");
        rows.Select(r => r.Label).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Ordering_by_a_remapped_property_descending_reverses_it()
    {
        var store = NewStore();
        store.Create(Seed());

        var rows = store.Read(null, OrderBy<ORow>.ByDescending(x => x.Label), null, null).ToList();

        rows.Select(r => r.Label).Should().Equal("c", "b", "a");
    }

    [Fact]
    public void Remapped_property_is_emitted_under_its_column_name()
    {
        CreateSchema();
        var connector = Connector();

        connector.Select(typeof(ORow), (LambdaExpression?)null,
            OrderBy<ORow>.By(x => x.Label).ToDictionary(), null, null).ToList();

        _executed.Should().ContainSingle(t => t.Contains("ORDER BY label_col ASC"));
        _executed.Should().NotContain(t => t.Contains("ORDER BY Label"));
    }

    [Fact]
    public async Task Async_path_orders_by_a_remapped_property_too()
    {
        // AsyncDataBaseBulkStore reaches a different funnel (SelectAsync), so it needs its own guard and
        // its own proof — the sync fix does not cover it.
        var store = NewAsyncStore();
        await store.CreateAsync(Seed());

        var rows = (await store.ReadAsync(null, OrderBy<ORow>.By(x => x.Label), null, null)).ToList();

        rows.Select(r => r.Label).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Ordering_by_the_mapped_column_name_still_works()
    {
        // ByName("label_col") worked before the guard existed, so it must keep working — the column name
        // comes from the same metadata, so accepting it does not widen what can be emitted.
        var store = NewStore();
        store.Create(Seed());

        var rows = store.Read(null, OrderBy<ORow>.ByName("label_col"), null, null).ToList();

        rows.Select(r => r.Label).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Multi_key_sort_applies_the_keys_in_order()
    {
        var store = NewStore();
        store.Create(new List<ORow>
        {
            new ORow { Label = "a", Rank = 2 },
            new ORow { Label = "a", Rank = 1 },
            new ORow { Label = "b", Rank = 9 },
        });

        var rows = store.Read(null, OrderBy<ORow>.By(x => x.Label).ThenBy(x => x.Rank), null, null).ToList();

        rows.Select(r => r.Rank).Should().Equal(1, 2, 9);
    }

    // ------------------------------------------------------------ back-compat

    [Fact]
    public void An_ordinary_property_emits_exactly_what_it_emitted_before()
    {
        // The pin that keeps the fix honest: an unremapped property must produce "ORDER BY Rank ASC",
        // bare and unquoted, byte for byte. Quoting it — the originally prescribed remedy — would show up
        // here, and would break this sort on PostgreSQL.
        CreateSchema();
        var connector = Connector();

        connector.Select(typeof(ORow), (LambdaExpression?)null,
            OrderBy<ORow>.By(x => x.Rank).ToDictionary(), null, null).ToList();

        _executed.Should().ContainSingle(t => t.EndsWith("ORDER BY Rank ASC"));
    }

    [Fact]
    public void Ordering_by_an_ordinary_property_still_sorts()
    {
        var store = NewStore();
        store.Create(Seed());

        var rows = store.Read(null, OrderBy<ORow>.By(x => x.Rank), null, null).ToList();

        rows.Select(r => r.Rank).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void No_order_by_is_still_no_ORDER_BY_clause()
    {
        CreateSchema();
        var connector = Connector();

        connector.Select(typeof(ORow), (LambdaExpression?)null, null, null, null).ToList();

        _executed.Should().NotContain(t => t.Contains("ORDER BY"));
    }

    // ------------------------------------------------------------------ SH-H003

    [Fact]
    public void A_batch_separator_payload_cannot_create_a_table()
    {
        // The measured pre-fix behaviour: this created Pwned, silently.
        var store = NewStore();
        store.Create(Seed());

        var act = () => store.Read(null, OrderBy<ORow>.ByName("Rank; CREATE TABLE Pwned (x INTEGER); --"), null, null).ToList();

        act.Should().Throw<ArgumentException>();
        TableExists("Pwned").Should().BeFalse("the injected statement must never reach the database");
    }

    [Fact]
    public void A_comment_payload_cannot_override_the_callers_limit()
    {
        // The measured pre-fix behaviour: "Rank LIMIT 1 --" returned 1 row while the caller asked for 100,
        // because the attacker's LIMIT landed ahead of the framework's and the rest was commented out.
        var store = NewStore();
        store.Create(Seed());

        var act = () => store.Read(null, OrderBy<ORow>.ByName("Rank LIMIT 1 --"), 100, null).ToList();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_subquery_payload_is_rejected()
    {
        // ORDER BY accepts arbitrary expressions, so a sort key is a general-purpose evaluation sink even
        // without a statement separator — this one executed and reordered the rows before the fix.
        var store = NewStore();
        store.Create(Seed());

        var act = () => store.Read(null, OrderBy<ORow>.ByName("(SELECT count(*) FROM sqlite_master)"), null, null).ToList();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_async_path_rejects_the_same_payload()
    {
        var store = NewAsyncStore();
        await store.CreateAsync(Seed());

        var act = async () => await store.ReadAsync(null, OrderBy<ORow>.ByName("Rank; CREATE TABLE Pwned (x INTEGER); --"), null, null);

        await act.Should().ThrowAsync<ArgumentException>();
        TableExists("Pwned").Should().BeFalse();
    }

    [Fact]
    public void An_unknown_sort_key_fails_before_reaching_the_database()
    {
        var store = NewStore();
        store.Create(Seed());

        var act = () => store.Read(null, OrderBy<ORow>.ByName("NoSuchThing"), null, null).ToList();

        // Not a SqliteException: the framework must name the key and the type, rather than letting the
        // provider report a column the developer never wrote.
        act.Should().Throw<ArgumentException>()
            .WithMessage("*NoSuchThing*")
            .WithMessage("*ORow*");
        act.Should().NotThrow<SqliteException>();
    }
}
