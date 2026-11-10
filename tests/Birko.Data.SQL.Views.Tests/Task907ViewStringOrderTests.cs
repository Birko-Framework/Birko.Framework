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
/// TASK-907 — a view's string <c>MIN</c> / <c>MAX</c> is taken in Ordinal order (UTF-16 code units), as the store's
/// <c>Aggregate</c> takes it by default (the user's decision, 2026-11-10), on every SQL provider, on the fly and persistent.
/// A view has no in-memory twin, so it has no other order to offer.
/// <para>
/// Each group discriminates one way a collation can differ from Ordinal: case, accents, a trailing space (a PAD SPACE
/// collation ties <c>"a"</c> and <c>"a "</c>), <c>ß</c> against <c>ss</c>, a character above U+FFFF against one in
/// U+E000–U+FFFF (UTF-16 puts the surrogate pair below it, a code-point order above), a null beside a value. Measured
/// before: every server answered in its column's collation (see the task file for the table).
/// </para>
/// </summary>
[Collection("T907")]
public abstract class Task907ViewStringOrderTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task907ViewStringOrderTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IAsyncBulkStore<T907Row> AsyncStore(PasswordSettings settings);

    [Table("T907VRows")]
    public class T907Row : AbstractModel
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
    }

    public class T907View
    {
        public string? Kind { get; set; }
        public string? Low { get; set; }
        public string? High { get; set; }
    }

    private static readonly (string Kind, string? Name)[] Seed =
    {
        ("case", "apple"), ("case", "Banana"),
        ("accent", "éclair"), ("accent", "zebra"),
        ("word", "zebra"), ("word", "Zebra"),
        ("space", "a "), ("space", "a"),
        ("eszett", "ß"), ("eszett", "ss"),
        ("astral", "\U0001F600"), ("astral", "～"),
        ("nulls", null), ("nulls", "m"),
        ("allnull", null),
    };

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
        foreach (var (kind, name) in Seed) await store.CreateAsync(new T907Row { Kind = kind, Name = name });
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
        try { _connector!.DropTable(new[] { typeof(T907Row) }); } catch { }
    }

    private static string Show(string? value)
        => value == null ? "null" : "\"" + string.Concat(value.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString())) + "\"";

    [Theory]
    [InlineData(PortableMode.OnTheFly)]
    [InlineData(PortableMode.Persistent)]
    public async Task A_view_string_min_and_max_are_Ordinal(PortableMode mode)
    {
        if (_settings == null) return;
        var viewName = $"T907V{(int)mode}";
        var definition = new ViewDefinitionBuilder<T907View>().HasName(viewName).HasQueryMode(mode).From<T907Row>()
            .Select<T907Row, string?>(r => r.Kind, v => v.Kind)
            .GroupBy<T907Row, string?>(r => r.Kind)
            .Min<T907Row, string?>(r => r.Name, v => v.Low)
            .Max<T907Row, string?>(r => r.Name, v => v.High)
            .Build();

        var expected = string.Join(", ", Seed.GroupBy(s => s.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var values = g.Select(s => s.Name).Where(n => n != null).ToList();
            var low = values.Count == 0 ? null : values.Min(StringComparer.Ordinal);
            var high = values.Count == 0 ? null : values.Max(StringComparer.Ordinal);
            return $"{g.Key}={Show(low)}..{Show(high)}";
        }));

        string actual;
        try
        {
            if (mode == PortableMode.Persistent)
            {
                _views.Add(viewName);
                await new SqlViewManager(_connector!).EnsureAsync(definition);
            }
            var rows = (await new SqlViewStore<T907View>(_connector!, definition).QueryAsync()).ToList();
            actual = string.Join(", ", rows.OrderBy(r => r.Kind, StringComparer.Ordinal).Select(r => $"{r.Kind}={Show(r.Low)}..{Show(r.High)}"));
        }
        catch (Exception ex)
        {
            actual = $"{ex.GetType().Name}: {ex.Message}";
        }

        Output.WriteLine($"MEASURE | {Provider} | {mode} | {actual} | expected {expected}");
        actual.Should().Be(expected);
    }
}

public sealed class SqliteTask907ViewStringOrderTests : Task907ViewStringOrderTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t907-" + Guid.NewGuid().ToString("N"));
    public SqliteTask907ViewStringOrderTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t907.db");
    protected override IAsyncBulkStore<T907Row> AsyncStore(PasswordSettings settings) { var s = new AsyncSQLiteStore<T907Row>(); s.SetSettings(settings); return s; }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask907ViewStringOrderTests : Task907ViewStringOrderTestsBase
{
    public PostgreSqlTask907ViewStringOrderTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IAsyncBulkStore<T907Row> AsyncStore(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T907Row>(); s.SetSettings(settings); return s; }
}

public sealed class MySqlTask907ViewStringOrderTests : Task907ViewStringOrderTestsBase
{
    public MySqlTask907ViewStringOrderTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IAsyncBulkStore<T907Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMySQLStore<T907Row>(); s.SetSettings(settings); return s; }
}

public sealed class MSSqlTask907ViewStringOrderTests : Task907ViewStringOrderTestsBase
{
    public MSSqlTask907ViewStringOrderTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IAsyncBulkStore<T907Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMSSqlStore<T907Row>(); s.SetSettings(settings); return s; }
}
