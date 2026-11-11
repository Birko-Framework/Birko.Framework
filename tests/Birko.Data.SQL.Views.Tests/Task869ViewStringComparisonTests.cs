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
/// TASK-869 — a <see cref="SqlViewStore{TView}"/> filter with <c>StringComparison.Ordinal</c> / <c>OrdinalIgnoreCase</c>
/// matches what C# matches, on the OnTheFly and the persistent path; a culture comparison is refused by name before anything
/// runs. Before, the comparison was ignored on both paths and the column's collation decided (case-insensitive on SQLite,
/// MySQL and SQL Server, case-sensitive on PostgreSQL).
/// </summary>
[Collection("T869V")]
public abstract class Task869ViewStringComparisonTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task869ViewStringComparisonTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    public class T869VItem : AbstractModel
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
    }

    public class T869VView
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
    }

    private sealed class ItemMapping : IModelMapping<T869VItem>
    {
        public void Configure(ModelMap<T869VItem> map)
        {
            map.ToTable("T869VItems").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Code);
            map.Property(x => x.Hits);
        }
    }

    private static ViewDefinition Definition(PortableMode mode) => new ViewDefinitionBuilder<T869VView>()
        .HasName("T869VView").HasQueryMode(mode)
        .From<T869VItem>()
        .Select<T869VItem, string>(o => o.Code!, v => v.Code!)
        .Select<T869VItem, int>(o => o.Hits, v => v.Hits)
        .Build();

    public static readonly IReadOnlyDictionary<string, Expression<Func<T869VView, bool>>> Exact =
        new Dictionary<string, Expression<Func<T869VView, bool>>>
        {
            ["Contains(\"ab\", Ordinal)"] = v => v.Code!.Contains("ab", StringComparison.Ordinal),
            ["StartsWith(\"CAF\", OrdinalIgnoreCase)"] = v => v.Code!.StartsWith("CAF", StringComparison.OrdinalIgnoreCase),
            ["EndsWith(\"É\", OrdinalIgnoreCase)"] = v => v.Code!.EndsWith("É", StringComparison.OrdinalIgnoreCase),
            ["EndsWith(\"é\", Ordinal)"] = v => v.Code!.EndsWith("é", StringComparison.Ordinal),
            ["Equals(\"abc\", OrdinalIgnoreCase)"] = v => string.Equals(v.Code, "abc", StringComparison.OrdinalIgnoreCase),
            ["!Contains(\"a_\", Ordinal)"] = v => !v.Code!.Contains("a_", StringComparison.Ordinal),
            ["Contains(\"ı\", OrdinalIgnoreCase)"] = v => v.Code!.Contains("ı", StringComparison.OrdinalIgnoreCase),
        };

    public static IEnumerable<object[]> ExactNames => Exact.Keys.Select(k => new object[] { k });

    private AbstractConnector? _connector;
    private readonly List<T869VView> _seed = new();
    private readonly List<string> _executed = new();

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
        connector.CreateTable(new[] { typeof(T869VItem) });
        var hits = 0;
        foreach (var code in new[] { "abc", "ABC", "Abc", "café", "CAFÉ", "cafe", "a_b", "axb", "I", "i", "ı" })
        {
            var item = new T869VItem { Guid = Guid.NewGuid(), Code = code, Hits = hits++ };
            await ((AbstractAsyncConnector)connector).InsertAsync(typeof(T869VItem), item);
            _seed.Add(new T869VView { Code = item.Code, Hits = item.Hits });
        }
        await new SqlViewManager(connector).EnsureAsync(Definition(PortableMode.Persistent));
        connector.OnExecute += _executed.Add;
    }

    public async Task DisposeAsync()
    {
        if (_connector == null) return;
        _connector.OnExecute -= _executed.Add;
        await CleanAsync(_connector);
    }

    private static async Task CleanAsync(AbstractConnector connector)
    {
        try { await new SqlViewManager(connector).DropAsync("T869VView"); } catch { }
        try { connector.DropTable(new[] { typeof(T869VItem) }); } catch { }
    }

    private async Task Matches(PortableMode mode, string shape, Expression<Func<T869VView, bool>> filter)
    {
        var store = new SqlViewStore<T869VView>(_connector!, Definition(mode));
        var expected = _seed.Where(filter.Compile()).Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();
        (await store.QueryAsync(filter)).Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal).Should()
            .Equal(expected, $"{mode} '{shape}' must match the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count, $"{mode} '{shape}' must count the rows C# matches");
    }

    [Theory]
    [MemberData(nameof(ExactNames))]
    public async Task A_view_filter_with_an_ordinal_comparison_matches_what_CSharp_matches_on_both_paths(string shape)
    {
        if (_connector == null) return;
        await Matches(PortableMode.OnTheFly, shape, Exact[shape]);
        await Matches(PortableMode.Persistent, shape, Exact[shape]);
    }

    [Theory]
    [InlineData(PortableMode.OnTheFly)]
    [InlineData(PortableMode.Persistent)]
    public async Task A_view_filter_with_a_culture_comparison_is_refused_by_name_before_it_runs(PortableMode mode)
    {
        if (_connector == null) return;
        var store = new SqlViewStore<T869VView>(_connector, Definition(mode));
        _executed.Clear();

        var read = () => store.QueryAsync(v => v.Code!.Contains("caf", StringComparison.CurrentCultureIgnoreCase));
        (await read.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("CurrentCultureIgnoreCase");
        _executed.Should().NotContain(text => text.Contains("SELECT", StringComparison.OrdinalIgnoreCase), "nothing ran");
    }
}

public sealed class SqliteTask869ViewStringComparisonTests : Task869ViewStringComparisonTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t869v-{Guid.NewGuid():N}.db");
    public SqliteTask869ViewStringComparisonTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));
}

public sealed class PostgreSqlTask869ViewStringComparisonTests : Task869ViewStringComparisonTestsBase
{
    public PostgreSqlTask869ViewStringComparisonTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => new PostgreSQLConnector(new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        LiveDatabase.Name("PG"),
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 });
}

public sealed class MySqlTask869ViewStringComparisonTests : Task869ViewStringComparisonTestsBase
{
    public MySqlTask869ViewStringComparisonTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => new MySQLConnector(new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        LiveDatabase.Name("MYSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 });
}

public sealed class MSSqlTask869ViewStringComparisonTests : Task869ViewStringComparisonTestsBase
{
    public MSSqlTask869ViewStringComparisonTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => new MSSqlConnector(new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        LiveDatabase.Name("MSSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true });
}
