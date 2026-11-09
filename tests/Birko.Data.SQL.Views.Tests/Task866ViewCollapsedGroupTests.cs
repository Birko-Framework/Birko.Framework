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
/// TASK-866 — a <see cref="SqlViewStore{TView}"/> filter in which <c>&amp;&amp;</c> / <c>||</c> collapses onto a group (its other
/// operand is a constant) keeps that group's own separator, on the OnTheFly and the persistent path. Before, the collapse
/// wrote the collapsed operator's flag over it: <c>!((v.A == 1 || v.A == 2) &amp;&amp; t)</c> read every row.
/// </summary>
[Collection("T866V")]
public abstract class Task866ViewCollapsedGroupTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task866ViewCollapsedGroupTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    public class T866VItem : AbstractModel
    {
        public string? Code { get; set; }
        public int A { get; set; }
        public int? Score { get; set; }
    }

    public class T866VView
    {
        public string? Code { get; set; }
        public int A { get; set; }
        public int? Score { get; set; }
    }

    private sealed class ItemMapping : IModelMapping<T866VItem>
    {
        public void Configure(ModelMap<T866VItem> map)
        {
            map.ToTable("T866VItems").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Code);
            map.Property(x => x.A);
            map.Property(x => x.Score);
        }
    }

    private static ViewDefinition Definition(PortableMode mode) => new ViewDefinitionBuilder<T866VView>()
        .HasName("T866VView").HasQueryMode(mode)
        .From<T866VItem>()
        .Select<T866VItem, string>(o => o.Code!, v => v.Code!)
        .Select<T866VItem, int>(o => o.A, v => v.A)
        .Select<T866VItem, int?>(o => o.Score, v => v.Score)
        .Build();

    private static readonly bool T = true;
    private static readonly bool F = false;

    public static readonly IReadOnlyDictionary<string, Expression<Func<T866VView, bool>>> Shapes =
        new Dictionary<string, Expression<Func<T866VView, bool>>>
        {
            ["!((A == 1 || A == 2) && t)"] = v => !((v.A == 1 || v.A == 2) && T),
            ["A >= 1 && ((A <= 3 && Code != r2) || f)"] = v => v.A >= 1 && ((v.A <= 3 && v.Code != "r2") || F),
            ["!(A > 5 || ((A == 1 || A == 2) && t))"] = v => !(v.A > 5 || ((v.A == 1 || v.A == 2) && T)),
            ["!((Score == 5 || Score > 6) && t)"] = v => !((v.Score == 5 || v.Score > 6) && T),
        };

    public static IEnumerable<object[]> ShapeNames => Shapes.Keys.Select(k => new object[] { k });

    private AbstractConnector? _connector;
    private readonly List<T866VView> _seed = new();

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
        connector.CreateTable(new[] { typeof(T866VItem) });
        var scores = new int?[] { null, 5, null, 8, null, 2 };
        for (var i = 0; i < scores.Length; i++)
        {
            var item = new T866VItem { Guid = Guid.NewGuid(), Code = $"r{i + 1}", A = i + 1, Score = scores[i] };
            await ((AbstractAsyncConnector)connector).InsertAsync(typeof(T866VItem), item);
            _seed.Add(new T866VView { Code = item.Code, A = item.A, Score = item.Score });
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
        try { await new SqlViewManager(connector).DropAsync("T866VView"); } catch { }
        try { connector.DropTable(new[] { typeof(T866VItem) }); } catch { }
    }

    private async Task Matches(PortableMode mode, string shape, Expression<Func<T866VView, bool>> filter)
    {
        var store = new SqlViewStore<T866VView>(_connector!, Definition(mode));
        var expected = _seed.Where(filter.Compile()).Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();
        expected.Should().NotBeEmpty().And.HaveCountLessThan(_seed.Count, $"'{shape}' must constrain in C#");
        (await store.QueryAsync(filter)).Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal).Should()
            .Equal(expected, $"{mode} '{shape}' must match the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count, $"{mode} '{shape}' must count the rows C# matches");
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public async Task A_view_filter_that_collapses_onto_a_group_matches_what_CSharp_matches_on_both_paths(string shape)
    {
        if (_connector == null) return;
        await Matches(PortableMode.OnTheFly, shape, Shapes[shape]);
        await Matches(PortableMode.Persistent, shape, Shapes[shape]);
    }
}

public sealed class SqliteTask866ViewCollapsedGroupTests : Task866ViewCollapsedGroupTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t866v-{Guid.NewGuid():N}.db");
    public SqliteTask866ViewCollapsedGroupTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));
}

public sealed class PostgreSqlTask866ViewCollapsedGroupTests : Task866ViewCollapsedGroupTestsBase
{
    public PostgreSqlTask866ViewCollapsedGroupTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => new PostgreSQLConnector(new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 });
}

public sealed class MySqlTask866ViewCollapsedGroupTests : Task866ViewCollapsedGroupTestsBase
{
    public MySqlTask866ViewCollapsedGroupTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => new MySQLConnector(new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 });
}

public sealed class MSSqlTask866ViewCollapsedGroupTests : Task866ViewCollapsedGroupTestsBase
{
    public MSSqlTask866ViewCollapsedGroupTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => new MSSqlConnector(new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true });
}
