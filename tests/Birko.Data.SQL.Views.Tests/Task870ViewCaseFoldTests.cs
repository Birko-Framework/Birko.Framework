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
/// TASK-870 — an OnTheFly <see cref="SqlViewStore{TView}"/> filter over <c>ToLower()</c> / <c>ToUpper()</c> folds as the
/// provider's <c>SqlValueFragmentDialect.CaseFold</c> does: C#'s own fold on SQLite (whose <c>LOWER</c> folds ASCII only;
/// measured before, <c>v.Code.ToLower() == "čučoriedka"</c> matched only the lowercase row), and <c>LOWER</c> / <c>UPPER</c>
/// on the servers, which fold these texts as C# does. TASK-890: a persistent view folds its own column the same way; a fold of
/// a computed value is refused by name there, as every computed fragment is (TASK-858).
/// </summary>
[Collection("T870V")]
public abstract class Task870ViewCaseFoldTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task870ViewCaseFoldTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    public class T870VItem : AbstractModel
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
    }

    public class T870VView
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
    }

    private sealed class ItemMapping : IModelMapping<T870VItem>
    {
        public void Configure(ModelMap<T870VItem> map)
        {
            map.ToTable("T870VItems").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Code);
            map.Property(x => x.Hits);
        }
    }

    private static ViewDefinition Definition(PortableMode mode) => new ViewDefinitionBuilder<T870VView>()
        .HasName("T870VView").HasQueryMode(mode)
        .From<T870VItem>()
        .Select<T870VItem, string>(o => o.Code!, v => v.Code!)
        .Select<T870VItem, int>(o => o.Hits, v => v.Hits)
        .Build();

    public static readonly IReadOnlyDictionary<string, Expression<Func<T870VView, bool>>> Exact =
        new Dictionary<string, Expression<Func<T870VView, bool>>>
        {
            ["ToLower() == \"čučoriedka\""] = v => v.Code!.ToLower() == "čučoriedka",
            ["ToLower().Contains(\"čuč\")"] = v => v.Code!.ToLower().Contains("čuč"),
            ["ToUpperInvariant() == \"CAFÉ\""] = v => v.Code!.ToUpperInvariant() == "CAFÉ",
            ["ToLowerInvariant().StartsWith(\"ab\")"] = v => v.Code!.ToLowerInvariant().StartsWith("ab"),
            ["!(ToUpper() == \"ÄRGER\")"] = v => !(v.Code!.ToUpper() == "ÄRGER"),
            ["string.Equals(ToLower(), \"café\", Ordinal)"] = v => string.Equals(v.Code!.ToLower(), "café", StringComparison.Ordinal),
            ["string.Equals(ToUpper(), \"čučoriedka\", OrdinalIgnoreCase)"] = v => string.Equals(v.Code!.ToUpper(), "čučoriedka", StringComparison.OrdinalIgnoreCase),
            ["ToLower() != \"abc\""] = v => v.Code!.ToLower() != "abc",
        };

    public static IEnumerable<object[]> ExactNames => Exact.Keys.Select(k => new object[] { k });

    private AbstractConnector? _connector;
    private readonly List<T870VView> _seed = new();
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
        connector.CreateTable(new[] { typeof(T870VItem) });
        var hits = 0;
        foreach (var code in new[] { "abc", "ABC", "Čučoriedka", "ČUČORIEDKA", "čučoriedka", "Café", "CAFÉ", "Ärger", "ärger" })
        {
            var item = new T870VItem { Guid = Guid.NewGuid(), Code = code, Hits = hits++ };
            await ((AbstractAsyncConnector)connector).InsertAsync(typeof(T870VItem), item);
            _seed.Add(new T870VView { Code = item.Code, Hits = item.Hits });
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
        try { await new SqlViewManager(connector).DropAsync("T870VView"); } catch { }
        try { connector.DropTable(new[] { typeof(T870VItem) }); } catch { }
    }

    private async Task Matches(PortableMode mode, string shape, Expression<Func<T870VView, bool>> filter)
    {
        var store = new SqlViewStore<T870VView>(_connector!, Definition(mode));
        var expected = _seed.Where(filter.Compile()).Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();
        (await store.QueryAsync(filter)).Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal).Should()
            .Equal(expected, $"{mode} '{shape}' must match the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count, $"{mode} '{shape}' must count the rows C# matches");
    }

    [Theory]
    [MemberData(nameof(ExactNames))]
    public async Task An_OnTheFly_view_filter_over_a_case_fold_matches_what_CSharp_matches(string shape)
    {
        if (_connector == null) return;
        await Matches(PortableMode.OnTheFly, shape, Exact[shape]);
    }

    /// <summary>
    /// TASK-890 — a persistent (or Auto, once the view exists) view folds its own column: the fold's column is renamed to the
    /// view's. Before (TASK-870 to TASK-890) every shape was refused by name ("computes"), and before TASK-870 each failed with
    /// an error naming something else ("no such column: T870VItems.Code", 42P01, "could not be bound"). This test asserted the
    /// refusal; it is inverted, not deleted (rule 56).
    /// </summary>
    [Theory]
    [MemberData(nameof(ExactNames))]
    public async Task A_persistent_view_filter_over_a_case_fold_matches_what_CSharp_matches(string shape)
    {
        if (_connector == null) return;
        await Matches(PortableMode.Persistent, shape, Exact[shape]);
        await Matches(PortableMode.Auto, shape, Exact[shape]);
    }

    /// <summary>
    /// TASK-890 — a fold of a computed value (here, of another fold) has no column of the view to fold (TASK-858's rule for
    /// a computed fragment), so it is refused by name before anything runs.
    /// </summary>
    [Fact]
    public async Task A_persistent_view_filter_over_a_case_fold_of_a_computed_value_is_refused_by_name_before_it_runs()
    {
        if (_connector == null) return;
        var store = new SqlViewStore<T870VView>(_connector, Definition(PortableMode.Persistent));
        _executed.Clear();

        var read = () => store.QueryAsync(v => v.Code!.ToLower().ToUpper() == "ABC");
        (await read.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("computes");
        _executed.Should().NotContain(text => text.Contains("SELECT", StringComparison.OrdinalIgnoreCase), "nothing ran");
    }
}

public sealed class SqliteTask870ViewCaseFoldTests : Task870ViewCaseFoldTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t869v-{Guid.NewGuid():N}.db");
    public SqliteTask870ViewCaseFoldTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));
}

public sealed class PostgreSqlTask870ViewCaseFoldTests : Task870ViewCaseFoldTestsBase
{
    public PostgreSqlTask870ViewCaseFoldTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => new PostgreSQLConnector(new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 });
}

public sealed class MySqlTask870ViewCaseFoldTests : Task870ViewCaseFoldTestsBase
{
    public MySqlTask870ViewCaseFoldTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => new MySQLConnector(new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 });
}

public sealed class MSSqlTask870ViewCaseFoldTests : Task870ViewCaseFoldTestsBase
{
    public MSSqlTask870ViewCaseFoldTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => new MSSqlConnector(new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true });
}
