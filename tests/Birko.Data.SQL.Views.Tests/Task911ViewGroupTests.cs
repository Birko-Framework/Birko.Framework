using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using PortableMode = Birko.Data.Views.ViewQueryMode;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-911 — an aggregating view grouped by a string makes exactly the groups C# makes (Ordinal), on every SQL provider, on
/// the fly and persistent: grouped by one string, by two strings, and by a string and a number. Each group answers its own
/// key text, its count, sum and string MIN; the view's count is the number of groups, and a filter on the key still
/// selects exactly that key. The keys are distinct in C# and twins under some collation: case, a trailing space, the empty
/// string against a space, a no-break space, <c>ß</c> / <c>ss</c>, a decomposed <c>é</c>, the Kelvin sign, U+E000 and
/// U+1F600 (weightless in SQL Server's default collation), U+FF5E and null. Measured before: see the task file.
/// </summary>
[Collection("T907")]
public abstract class Task911ViewGroupTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task911ViewGroupTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IAsyncBulkStore<T911Row> AsyncStore(PasswordSettings settings);

    [Table("T911VRows")]
    public class T911Row : AbstractModel
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public int N { get; set; }
    }

    public class T911View
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public int N { get; set; }
        public int Count { get; set; }
        public int Total { get; set; }
        public string? Low { get; set; }
    }

    private static readonly string?[] Keys =
    {
        "apple", "APPLE", "Zebra", "zebra", "a", "a ", "a\t", "", " ", " ", "ß", "ss", "café", "café", "K", "K",
        "", char.ConvertFromUtf32(0x1F600), "～", null,
    };

    /// <summary>Key <c>i</c> holds <c>i % 3 + 1</c> rows; Names are case twins (x, X, x); N is 1 or 2.</summary>
    private static IEnumerable<T911Row> Seed()
    {
        for (var i = 0; i < Keys.Length; i++)
        {
            for (var j = 0; j <= i % 3; j++) yield return new T911Row { Kind = Keys[i], Name = j % 2 == 0 ? "x" : "X", N = j % 2 + 1 };
        }
    }

    private PasswordSettings? _settings;
    private AbstractConnector? _connector;
    private readonly List<string> _views = new();

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
        _settings = Settings();
        var store = AsyncStore(_settings);
        _connector = ((ISqlConnectedStore)store).Connector!;
        Clean();
        foreach (var row in Seed()) await store.CreateAsync(row);
    }

    public async Task DisposeAsync()
    {
        if (_settings == null) return;
        foreach (var view in _views)
        {
            try { await new SqlViewManager(_connector!).DropAsync(view); } catch { }
        }
        Clean();
    }

    private void Clean()
    {
        try { _connector!.DropTable(new[] { typeof(T911Row) }); } catch { }
    }

    private static string Show(string? value)
        => value == null ? "null" : "\"" + string.Concat(value.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString())) + "\"";

    public static IEnumerable<object[]> Cases
        => from mode in new[] { PortableMode.OnTheFly, PortableMode.Persistent }
           from shape in new[] { "Kind", "Kind+Name", "Kind+N" }
           select new object[] { mode, shape };

    private static ViewDefinition Definition(string name, PortableMode mode, string shape)
    {
        var builder = new ViewDefinitionBuilder<T911View>().HasName(name).HasQueryMode(mode).From<T911Row>()
            .Select<T911Row, string?>(r => r.Kind, v => v.Kind)
            .GroupBy<T911Row, string?>(r => r.Kind);
        if (shape == "Kind+Name") builder = builder.Select<T911Row, string?>(r => r.Name, v => v.Name).GroupBy<T911Row, string?>(r => r.Name);
        if (shape == "Kind+N") builder = builder.Select<T911Row, int>(r => r.N, v => v.N).GroupBy<T911Row, int>(r => r.N);
        return builder
            .Count<T911Row>(v => v.Count)
            .Sum<T911Row, int>(r => r.N, v => v.Total)
            .Min<T911Row, string?>(r => r.Name, v => v.Low)
            .Build();
    }

    private static string Render(string shape, string? kind, string? name, int n, int count, int total, string? low)
        => $"{Show(kind)}{(shape == "Kind+Name" ? "/" + Show(name) : "")}{(shape == "Kind+N" ? "/" + n : "")}: {count} {total} {Show(low)}";

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task An_aggregating_view_grouped_by_a_string_makes_exactly_the_groups_CSharp_makes(PortableMode mode, string shape)
    {
        if (_settings == null) return;
        var viewName = $"T911V{(int)mode}{shape.Replace("+", "")}";
        var definition = Definition(viewName, mode, shape);

        var seed = Seed().ToList();
        var expected = seed
            .GroupBy(r => (r.Kind, Name: shape == "Kind+Name" ? r.Name : null, N: shape == "Kind+N" ? r.N : 0))
            .Select(g => Render(shape, g.Key.Kind, g.Key.Name, g.Key.N, g.Count(), g.Sum(r => r.N),
                g.Select(r => r.Name).Where(x => x != null).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault()))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var expectedApple = expected.Where(e => e.StartsWith("\"apple\"", StringComparison.Ordinal)).ToList();

        List<string> actual;
        long count;
        List<string> apple;
        try
        {
            if (mode == PortableMode.Persistent && !_views.Contains(viewName))
            {
                _views.Add(viewName);
                await new SqlViewManager(_connector!).EnsureAsync(definition);
            }
            var store = new SqlViewStore<T911View>(_connector!, definition);
            actual = (await store.QueryAsync()).Select(v => Render(shape, v.Kind, v.Name, v.N, v.Count, v.Total, v.Low))
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            count = await store.CountAsync();
            apple = (await store.QueryAsync(v => v.Kind == "apple")).Select(v => Render(shape, v.Kind, v.Name, v.N, v.Count, v.Total, v.Low))
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            actual = new List<string> { $"{ex.GetType().Name}: {ex.Message}" };
            count = -1;
            apple = new List<string>();
        }

        var missing = expected.Except(actual).ToList();
        var extra = actual.Except(expected).ToList();
        Output.WriteLine($"MEASURE | {Provider} | {mode} | {shape} | {actual.Count} groups (count {count}) where C# makes {expected.Count} | "
                         + $"missing [{string.Join(" | ", missing)}] | extra [{string.Join(" | ", extra)}]");
        actual.Should().Equal(expected);
        count.Should().Be(expected.Count);
        apple.Should().Equal(expectedApple);
    }
}

public sealed class SqliteTask911ViewGroupTests : Task911ViewGroupTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t911-" + Guid.NewGuid().ToString("N"));
    public SqliteTask911ViewGroupTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t911.db");
    protected override IAsyncBulkStore<T911Row> AsyncStore(PasswordSettings settings) { var s = new AsyncSQLiteStore<T911Row>(); s.SetSettings(settings); return s; }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask911ViewGroupTests : Task911ViewGroupTestsBase
{
    public PostgreSqlTask911ViewGroupTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IAsyncBulkStore<T911Row> AsyncStore(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T911Row>(); s.SetSettings(settings); return s; }
}

public sealed class MySqlTask911ViewGroupTests : Task911ViewGroupTestsBase
{
    public MySqlTask911ViewGroupTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IAsyncBulkStore<T911Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMySQLStore<T911Row>(); s.SetSettings(settings); return s; }
}

public sealed class MSSqlTask911ViewGroupTests : Task911ViewGroupTestsBase
{
    public MSSqlTask911ViewGroupTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IAsyncBulkStore<T911Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMSSqlStore<T911Row>(); s.SetSettings(settings); return s; }
}
