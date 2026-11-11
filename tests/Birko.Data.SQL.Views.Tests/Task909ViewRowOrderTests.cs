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
/// TASK-909 — the rows of an aggregating view ordered by a string group key or a string <c>MIN</c> are in Ordinal order
/// (UTF-16 code units, null first ascending), as the store's <c>Aggregate</c> orders them (the user's decision, 2026-11-11),
/// before the page is cut, on every SQL provider, on the fly and persistent. A view's string order is always Ordinal
/// (TASK-907): it has no comparison to ask for.
/// <para>
/// The keys discriminate case, accents, a tab, <c>ß</c> against <c>st</c>, U+1F600 against U+FF5E (UTF-16
/// puts the surrogate pair below it, code-point order above), null, and also a trailing space, <c>Zebra</c>, <c>ss</c>,
/// U+E000 and the empty string, which SQL Server's or MySQL's GROUP BY merged with another key until TASK-911 (the keys
/// were left out until then). Measured before: see the task file.
/// </para>
/// </summary>
[Collection("T907")]
public abstract class Task909ViewRowOrderTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task909ViewRowOrderTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IAsyncBulkStore<T909Row> AsyncStore(PasswordSettings settings);

    [Table("T909VRows")]
    public class T909Row : AbstractModel
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
    }

    public class T909View
    {
        public string? Kind { get; set; }
        public string? Low { get; set; }
    }

    private static readonly string Astral = char.ConvertFromUtf32(0x1F600);
    private static readonly string HighBmp = ((char)0xFF5E).ToString();
    private static readonly string PrivateUse = ((char)0xE000).ToString();

    /// <summary>Groups g00.. holding one value each (the MINs), and groups keyed by a discriminating value holding that value.</summary>
    private static IEnumerable<(string? Kind, string? Name)> Seed()
    {
        var values = new[] { "apple", "Banana", "éclair", "zebra", "Zebra", "a", "a ", "a\t", "ß", "ss", "st", "", Astral, HighBmp, PrivateUse, null };
        for (var i = 0; i < values.Length; i++) yield return ($"g{i:00}", values[i]);
        foreach (var key in values) yield return (key, key);
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
        foreach (var (kind, name) in Seed()) await store.CreateAsync(new T909Row { Kind = kind, Name = name });
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
        try { _connector!.DropTable(new[] { typeof(T909Row) }); } catch { }
    }

    private static string Show(string? value)
        => value == null ? "null" : "\"" + string.Concat(value.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString())) + "\"";

    private static int Compare(string? x, string? y) => x == null ? (y == null ? 0 : -1) : y == null ? 1 : string.CompareOrdinal(x, y);

    public static IEnumerable<object[]> Cases
        => from mode in new[] { PortableMode.OnTheFly, PortableMode.Persistent }
           from key in new[] { "Kind", "Low" }
           from desc in new[] { false, true }
           from paged in new[] { false, true }
           select new object[] { mode, key, desc, paged };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task An_aggregating_view_is_ordered_by_a_string_in_Ordinal_order_before_the_page(PortableMode mode, string key, bool desc, bool paged)
    {
        if (_settings == null) return;
        var viewName = $"T909V{(int)mode}";
        var definition = new ViewDefinitionBuilder<T909View>().HasName(viewName).HasQueryMode(mode).From<T909Row>()
            .Select<T909Row, string?>(r => r.Kind, v => v.Kind)
            .GroupBy<T909Row, string?>(r => r.Kind)
            .Min<T909Row, string?>(r => r.Name, v => v.Low)
            .Build();

        // By Low, then by Kind ascending: groups tie on Low (g00 and "apple" both hold "apple").
        var groups = Seed().GroupBy(s => s.Kind).Select(g => (Kind: g.Key, Low: g.Select(s => s.Name).Where(n => n != null).OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault())).ToList();
        groups.Sort((a, b) =>
        {
            var first = key == "Kind" ? Compare(a.Kind, b.Kind) : Compare(a.Low, b.Low);
            first *= desc ? -1 : 1;
            return first != 0 || key == "Kind" ? first : Compare(a.Kind, b.Kind);
        });
        int? limit = paged ? 6 : null;
        int? offset = paged ? 9 : null;
        var expected = string.Join(" ", groups.Skip(offset ?? 0).Take(limit ?? int.MaxValue).Select(g => $"{Show(g.Kind)}={Show(g.Low)}"));

        var orderBy = key == "Kind"
            ? (desc ? OrderBy<T909View>.ByDescending(v => v.Kind) : OrderBy<T909View>.By(v => v.Kind))
            : (desc ? OrderBy<T909View>.ByDescending(v => v.Low) : OrderBy<T909View>.By(v => v.Low)).ThenBy(v => v.Kind);

        string actual;
        try
        {
            if (mode == PortableMode.Persistent && !_views.Contains(viewName))
            {
                _views.Add(viewName);
                await new SqlViewManager(_connector!).EnsureAsync(definition);
            }
            var rows = (await new SqlViewStore<T909View>(_connector!, definition).QueryAsync(null, orderBy, limit, offset)).ToList();
            actual = string.Join(" ", rows.Select(r => $"{Show(r.Kind)}={Show(r.Low)}"));
        }
        catch (Exception ex)
        {
            actual = $"{ex.GetType().Name}: {ex.Message}";
        }

        Output.WriteLine($"MEASURE | {Provider} | {mode} | by {key} {(desc ? "desc" : "asc")} {(paged ? "paged" : "all")} | {actual} | expected {expected}");
        actual.Should().Be(expected);
    }
}

public sealed class SqliteTask909ViewRowOrderTests : Task909ViewRowOrderTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t909-" + Guid.NewGuid().ToString("N"));
    public SqliteTask909ViewRowOrderTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t909.db");
    protected override IAsyncBulkStore<T909Row> AsyncStore(PasswordSettings settings) { var s = new AsyncSQLiteStore<T909Row>(); s.SetSettings(settings); return s; }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask909ViewRowOrderTests : Task909ViewRowOrderTestsBase
{
    public PostgreSqlTask909ViewRowOrderTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        LiveDatabase.Name("PG"),
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IAsyncBulkStore<T909Row> AsyncStore(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T909Row>(); s.SetSettings(settings); return s; }
}

public sealed class MySqlTask909ViewRowOrderTests : Task909ViewRowOrderTestsBase
{
    public MySqlTask909ViewRowOrderTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        LiveDatabase.Name("MYSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IAsyncBulkStore<T909Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMySQLStore<T909Row>(); s.SetSettings(settings); return s; }
}

public sealed class MSSqlTask909ViewRowOrderTests : Task909ViewRowOrderTestsBase
{
    public MSSqlTask909ViewRowOrderTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        LiveDatabase.Name("MSSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IAsyncBulkStore<T909Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMSSqlStore<T909Row>(); s.SetSettings(settings); return s; }
}
