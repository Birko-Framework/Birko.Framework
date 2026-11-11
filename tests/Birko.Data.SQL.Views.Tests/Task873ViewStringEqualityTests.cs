using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Views;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using PortableMode = Birko.Data.Views.ViewQueryMode;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-873 — a <see cref="SqlViewStore{TView}"/> filter with a string <c>==</c> / <c>!=</c>, an <c>IN</c> list, a comparison
/// of two string columns or a case fold compared with a value matches what C# matches, on the OnTheFly and the persistent
/// path. Before, <c>=</c> followed the collation on both: on SQL Server <c>v.Code == "abc"</c> read <c>ABC</c> and
/// <c>abc </c>, on MySQL also <c>cafe</c> for <c>café</c>.
/// </summary>
[Collection("T873V")]
public abstract class Task873ViewStringEqualityTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task873ViewStringEqualityTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    public class T873VItem : AbstractModel
    {
        public string? Code { get; set; }
        public string? Other { get; set; }
        public int Hits { get; set; }
    }

    public class T873VView
    {
        public string? Code { get; set; }
        public string? Other { get; set; }
        public int Hits { get; set; }
    }

    private sealed class ItemMapping : IModelMapping<T873VItem>
    {
        public void Configure(ModelMap<T873VItem> map)
        {
            map.ToTable("T873VItems").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Code);
            map.Property(x => x.Other);
            map.Property(x => x.Hits);
        }
    }

    private static ViewDefinition Definition(PortableMode mode) => new ViewDefinitionBuilder<T873VView>()
        .HasName("T873VView").HasQueryMode(mode)
        .From<T873VItem>()
        .Select<T873VItem, string>(o => o.Code!, v => v.Code!)
        .Select<T873VItem, string>(o => o.Other!, v => v.Other!)
        .Select<T873VItem, int>(o => o.Hits, v => v.Hits)
        .Build();

    private static readonly string[] Some = { "abc", "café", "ß" };

    public static readonly IReadOnlyDictionary<string, Expression<Func<T873VView, bool>>> Shapes =
        new Dictionary<string, Expression<Func<T873VView, bool>>>
        {
            ["Code == \"abc\""] = v => v.Code == "abc",
            ["Code != \"abc\""] = v => v.Code != "abc",
            ["Code == \"café\""] = v => v.Code == "café",
            ["Code == \"ss\""] = v => v.Code == "ss",
            ["Code == \"\""] = v => v.Code == "",
            ["{abc, café, ß}.Contains(Code)"] = v => Some.Contains(v.Code!),
            ["!{abc, café, ß}.Contains(Code)"] = v => !Some.Contains(v.Code!),
            ["Code == Other"] = v => v.Code == v.Other,
            ["Code != Other"] = v => v.Code != v.Other,
            ["Code.ToLower() == \"abc\""] = v => v.Code != null && v.Code.ToLower() == "abc",
            ["Code.Equals(\"ABC\")"] = v => v.Code != null && v.Code.Equals("ABC"),
            ["!(Code == \"abc\" && Hits > 0)"] = v => !(v.Code == "abc" && v.Hits > 0),
        };

    public static IEnumerable<object[]> ShapeNames => Shapes.Keys.Select(k => new object[] { k });

    private AbstractConnector? _connector;
    private readonly List<T873VView> _seed = new();

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
        var registry = new ModelMapRegistry();
        registry.Register(new ItemMapping());
        registry.ApplyToDatabase();

        var connector = Connector();
        _connector = connector;
        await CleanAsync(connector);
        connector.CreateTable(new[] { typeof(T873VItem) });
        var codes = new[] { "abc", "ABC", "Abc", "abc ", "café", "CAFÉ", "cafe", "ß", "ss", "SS", "", " ", null };
        var others = new[] { "abc", "abc", "ABC", "abc", "cafe", "CAFÉ", "café", "ss", "ss", "ß", " ", "", "x" };
        for (var i = 0; i < codes.Length; i++)
        {
            var item = new T873VItem { Guid = Guid.NewGuid(), Code = codes[i], Other = others[i], Hits = i };
            await ((AbstractAsyncConnector)connector).InsertAsync(typeof(T873VItem), item);
            _seed.Add(new T873VView { Code = item.Code, Other = item.Other, Hits = item.Hits });
        }
        await new SqlViewManager(connector).EnsureAsync(Definition(PortableMode.Persistent));
    }

    public async Task DisposeAsync()
    {
        if (_connector == null) return;
        await CleanAsync(_connector);
    }

    private static async Task CleanAsync(AbstractConnector connector)
    {
        try { await new SqlViewManager(connector).DropAsync("T873VView"); } catch { }
        try { connector.DropTable(new[] { typeof(T873VItem) }); } catch { }
    }

    private static string Key(T873VView v) => $"[{v.Code ?? "<null>"}]|[{v.Other ?? "<null>"}]";

    private async Task Matches(PortableMode mode, string shape, Expression<Func<T873VView, bool>> filter)
    {
        var store = new SqlViewStore<T873VView>(_connector!, Definition(mode));
        var expected = _seed.Where(filter.Compile()).Select(Key).OrderBy(c => c, StringComparer.Ordinal).ToList();
        (await store.QueryAsync(filter)).Select(Key).OrderBy(c => c, StringComparer.Ordinal).Should()
            .Equal(expected, $"{mode} '{shape}' must match the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count, $"{mode} '{shape}' must count the rows C# matches");
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public async Task A_view_filter_with_a_string_equality_matches_what_CSharp_matches_on_both_paths(string shape)
    {
        if (_connector == null) return;
        await Matches(PortableMode.OnTheFly, shape, Shapes[shape]);
        // A persistent view refuses a filter that computes over its source columns (TASK-858), the case fold among them.
        if (shape.Contains("ToLower", StringComparison.Ordinal)) return;
        await Matches(PortableMode.Persistent, shape, Shapes[shape]);
    }
}

public sealed class SqliteTask873ViewStringEqualityTests : Task873ViewStringEqualityTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t873v-{Guid.NewGuid():N}.db");
    public SqliteTask873ViewStringEqualityTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => Task873Connectors.Sqlite(_path);
}

public sealed class PostgreSqlTask873ViewStringEqualityTests : Task873ViewStringEqualityTestsBase
{
    public PostgreSqlTask873ViewStringEqualityTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => Task873Connectors.PostgreSql();
}

public sealed class MySqlTask873ViewStringEqualityTests : Task873ViewStringEqualityTestsBase
{
    public MySqlTask873ViewStringEqualityTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => Task873Connectors.MySql();
}

public sealed class MSSqlTask873ViewStringEqualityTests : Task873ViewStringEqualityTestsBase
{
    public MSSqlTask873ViewStringEqualityTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => Task873Connectors.MSSql();
}

/// <summary>The live connectors TASK-873's tests use, configured as live-tests.yml does.</summary>
internal static class Task873Connectors
{
    public static AbstractConnector Sqlite(string path)
        => new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(path)!, Path.GetFileName(path)));

    public static AbstractConnector PostgreSql() => new PostgreSQLConnector(new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        LiveDatabase.Name("PG"),
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 });

    public static AbstractConnector MySql() => new MySQLConnector(new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        LiveDatabase.Name("MYSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 });

    public static AbstractConnector MSSql() => new MSSqlConnector(new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        LiveDatabase.Name("MSSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true });
}
