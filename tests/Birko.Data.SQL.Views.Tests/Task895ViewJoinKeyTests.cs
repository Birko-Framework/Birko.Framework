using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.Views;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using PortableMode = Birko.Data.Views.ViewQueryMode;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-895 — a view that joins two tables on string keys joins them exactly, as C#'s ordinal <c>==</c> (a LINQ join)
/// does, whether the join comes from <see cref="ViewDefinitionBuilder{TView}"/> or from a <c>[View]</c> attribute, read on
/// the fly or through the persistent view's DDL. The default is exact (TASK-873, the user's decision).
/// <para>
/// Before (measured): both join producers rendered a plain <c>=</c>, which follows the collation, so on SQL Server an item
/// <c>abc</c> joined the owners <c>abc</c>, <c>ABC</c> and <c>abc </c> and on MySQL also <c>café</c> / <c>cafe</c>; SQLite and
/// PostgreSQL were already exact. The exact form keeps the plain <c>=</c> in front, so the join still seeks the key's index
/// (measured on PostgreSQL, MySQL and SQL Server, 100,000 rows; SQL Server also creates an indexed view over it).
/// </para>
/// </summary>
[Collection("T895V")]
public abstract class Task895ViewJoinKeyTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task895ViewJoinKeyTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    [Table("T895VItems")]
    public class T895VItem : AbstractModel
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
    }

    [Table("T895VOwners")]
    public class T895VOwner : AbstractModel
    {
        public string? Code { get; set; }
        public string? Label { get; set; }
    }

    public class T895VView
    {
        public string? Code { get; set; }
        public string? Label { get; set; }
        public int Hits { get; set; }
    }

    [View(typeof(T895VItem), typeof(T895VOwner), nameof(T895VItem.Code), nameof(T895VOwner.Code), name: "T895VAttrView")]
    public class T895VAttrView
    {
        [ViewField(typeof(T895VItem), nameof(T895VItem.Code))]
        public string? Code { get; set; }

        [ViewField(typeof(T895VOwner), nameof(T895VOwner.Label))]
        public string? Label { get; set; }

        [ViewField(typeof(T895VItem), nameof(T895VItem.Hits))]
        public int Hits { get; set; }
    }

    private static ViewDefinition Fluent(PortableMode mode, string name = "T895VView") => new ViewDefinitionBuilder<T895VView>()
        .HasName(name).HasQueryMode(mode)
        .From<T895VItem>()
        .Join<T895VItem, T895VOwner, string?>(i => i.Code, o => o.Code)
        .Select<T895VItem, string>(i => i.Code!, v => v.Code!)
        .Select<T895VOwner, string>(o => o.Label!, v => v.Label!)
        .Select<T895VItem, int>(i => i.Hits, v => v.Hits)
        .Build();

    private static readonly string?[] OwnerCodes = { "abc", "ABC", "abc ", "café", "cafe", "ß", "ss", "", "K", "a b", "café", null };

    private static readonly string?[] ItemCodes = { "abc", "ABC", "Abc", "abc ", "café", "CAFÉ", "cafe", "ss", "ß", "SS", "", " ", "k", "K", "K", "a b", "a b", "café", null };

    private AbstractConnector? _connector;
    private readonly List<T895VItem> _items = new();
    private readonly List<T895VOwner> _owners = new();

    private bool Live()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public async Task InitializeAsync()
    {
        if (!Live()) return;
        var connector = Connector();
        _connector = connector;
        await CleanAsync(connector);
        connector.CreateTable(new[] { typeof(T895VItem), typeof(T895VOwner) });
        for (var i = 0; i < OwnerCodes.Length; i++)
        {
            var owner = new T895VOwner { Guid = Guid.NewGuid(), Code = OwnerCodes[i], Label = "L" + i };
            await ((AbstractAsyncConnector)connector).InsertAsync(typeof(T895VOwner), owner);
            _owners.Add(owner);
        }
        for (var i = 0; i < ItemCodes.Length; i++)
        {
            var item = new T895VItem { Guid = Guid.NewGuid(), Code = ItemCodes[i], Hits = i };
            await ((AbstractAsyncConnector)connector).InsertAsync(typeof(T895VItem), item);
            _items.Add(item);
        }
    }

    public async Task DisposeAsync()
    {
        if (_connector == null) return;
        await CleanAsync(_connector);
    }

    private static async Task CleanAsync(AbstractConnector connector)
    {
        foreach (var view in new[] { "T895VView", "T895VAttrView" })
        {
            try { await new SqlViewManager(connector).DropAsync(view); } catch { }
        }
        try { connector.DropTable(new[] { typeof(T895VItem), typeof(T895VOwner) }); } catch { }
    }

    private static string Key(string? code, string? label, int hits) => $"[{code ?? "<null>"}]|[{label ?? "<null>"}]|{hits}";

    /// <summary>The rows a LINQ inner join answers: C#'s ordinal string equality, and NULL joins nothing.</summary>
    private List<string> Expected()
        => _items.Join(_owners, i => i.Code, o => o.Code, (i, o) => (i, o))
            .Where(p => p.i.Code != null)
            .Select(p => Key(p.i.Code, p.o.Label, p.i.Hits)).OrderBy(k => k, StringComparer.Ordinal).ToList();

    private static List<string> Sorted(IEnumerable<string> keys) => keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    private static readonly Expression<Func<T895VView, bool>> Every = v => v.Hits >= 0;

    [Theory]
    [InlineData(PortableMode.OnTheFly)]
    [InlineData(PortableMode.Persistent)]
    public async Task A_fluent_view_joins_string_keys_as_CSharp_does(PortableMode mode)
    {
        if (_connector == null) return;
        if (mode == PortableMode.Persistent) await new SqlViewManager(_connector).EnsureAsync(Fluent(mode));
        var store = new SqlViewStore<T895VView>(_connector, Fluent(mode));

        Sorted((await store.QueryAsync(Every)).Select(v => Key(v.Code, v.Label, v.Hits)))
            .Should().Equal(Expected(), $"{mode}: the join must pair the rows a LINQ join pairs");
        (await store.CountAsync(Every)).Should().Be(Expected().Count);
    }

    [Fact]
    public void An_attribute_view_joins_string_keys_as_CSharp_does_on_the_fly()
    {
        if (_connector == null) return;
        Expression<Func<T895VAttrView, bool>> every = v => v.Hits >= 0;
        var rows = _connector.SelectView(typeof(T895VAttrView), every).Cast<T895VAttrView>();

        Sorted(rows.Select(v => Key(v.Code, v.Label, v.Hits))).Should().Equal(Expected());
    }

    [Fact]
    public async Task An_attribute_view_joins_string_keys_as_CSharp_does_in_its_persistent_DDL()
    {
        if (_connector == null) return;
        _connector.CreateView(typeof(T895VAttrView), "T895VAttrView");
        // Read the attribute-built view by name: a persistent definition of the same columns reads it as it stands.
        var store = new SqlViewStore<T895VView>(_connector, Fluent(PortableMode.Persistent, "T895VAttrView"));

        Sorted((await store.QueryAsync(Every)).Select(v => Key(v.Code, v.Label, v.Hits))).Should().Equal(Expected());
    }

    /// <summary>A join on keys that are not strings keeps the plain <c>=</c>: only text has a collation.</summary>
    [Fact]
    public void A_join_condition_is_marked_exact_only_for_string_keys()
    {
        var view = DataBase.LoadView(typeof(T895VAttrView))!;
        view.Join!.SelectMany(j => j.Conditions!).Single().Comparison.Should().Be(StringComparison.Ordinal);

        var guidJoin = new SqlViewTranslatorJoinProbe().GuidJoinCondition();
        guidJoin.Comparison.Should().BeNull();
    }

    /// <summary>A fluent join on Guid keys, translated.</summary>
    private sealed class SqlViewTranslatorJoinProbe
    {
        public Condition GuidJoinCondition()
        {
            var definition = new ViewDefinitionBuilder<T895VView>()
                .HasName("T895VGuidView")
                .From<T895VItem>()
                .Join<T895VItem, T895VOwner, Guid?>(i => i.Guid, o => o.Guid)
                .Select<T895VItem, int>(i => i.Hits, v => v.Hits)
                .Build();
            return SqlViewTranslator.Translate(definition).Join!.SelectMany(j => j.Conditions!).Single();
        }
    }
}

public sealed class SqliteTask895ViewJoinKeyTests : Task895ViewJoinKeyTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t895v-{Guid.NewGuid():N}.db");
    public SqliteTask895ViewJoinKeyTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => Task873Connectors.Sqlite(_path);
}

public sealed class PostgreSqlTask895ViewJoinKeyTests : Task895ViewJoinKeyTestsBase
{
    public PostgreSqlTask895ViewJoinKeyTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => Task873Connectors.PostgreSql();
}

public sealed class MySqlTask895ViewJoinKeyTests : Task895ViewJoinKeyTestsBase
{
    public MySqlTask895ViewJoinKeyTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => Task873Connectors.MySql();
}

public sealed class MSSqlTask895ViewJoinKeyTests : Task895ViewJoinKeyTestsBase
{
    public MSSqlTask895ViewJoinKeyTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => Task873Connectors.MSSql();
}
