using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
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
/// TASK-871 — every statement a SQL connector runs reaches <c>OnExecute</c> in one form: the diagnostic text with each
/// parameter's value written in place of its name (<c>DataBase.GetGeneratedQuery</c>).
/// <para>
/// Measured before on SQLite, PostgreSQL, MySQL and SQL Server: writes (insert, update, delete, filter update and delete)
/// passed the substituted text, but every read (by id, by filter, count, view read and view count, sync and async) passed
/// the raw command text, so a subscriber saw <c>@WHERECode0_0</c> where the value belonged.
/// </para>
/// </summary>
[Collection("T871")]
public abstract class Task871OnExecuteTextTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task871OnExecuteTextTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IBulkStore<T871Row> SyncStore(PasswordSettings settings);
    protected abstract IAsyncBulkStore<T871Row> AsyncStore(PasswordSettings settings);

    [Table("T871Rows")]
    public class T871Row : AbstractModel
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
    }

    public class T871View
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
    }

    private static ViewDefinition Definition(PortableMode mode) => new ViewDefinitionBuilder<T871View>()
        .HasName("T871View").HasQueryMode(mode)
        .From<T871Row>()
        .Select<T871Row, string?>(o => o.Code, v => v.Code)
        .Select<T871Row, int>(o => o.Hits, v => v.Hits)
        .Build();

    /// <summary>A parameter name left in the text: <c>@name</c>, not <c>@@ROWCOUNT</c>.</summary>
    private static readonly Regex Placeholder = new(@"(?<![@\w])@[A-Za-z_]\w*", RegexOptions.Compiled);

    private PasswordSettings? _settings;
    private IBulkStore<T871Row>? _sync;
    private IAsyncBulkStore<T871Row>? _async;
    private readonly List<AbstractConnector> _connectors = new();
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
        _settings = Settings();
        _sync = SyncStore(_settings);
        _async = AsyncStore(_settings);
        await CleanAsync();
        _sync.Create(new T871Row { Code = "seed", Hits = 1 });
        await _async.CreateAsync(new T871Row { Code = "seed-async", Hits = 1 });
        foreach (var store in new object[] { _sync, _async })
        {
            var connector = ((ISqlConnectedStore)store).Connector!;
            if (_connectors.Contains(connector)) continue;
            _connectors.Add(connector);
            connector.OnExecute += Record;
        }
        await new SqlViewManager(_connectors[0]).EnsureAsync(Definition(PortableMode.Persistent));
    }

    private void Record(string text)
    {
        lock (_executed) _executed.Add(text);
    }

    public async Task DisposeAsync()
    {
        foreach (var connector in _connectors) connector.OnExecute -= Record;
        if (_settings != null) await CleanAsync();
    }

    private async Task CleanAsync()
    {
        var connector = ((ISqlConnectedStore)_sync!).Connector!;
        try { await new SqlViewManager(connector).DropAsync("T871View"); } catch { }
        try { connector.DropTable(new[] { typeof(T871Row) }); } catch { }
    }

    /// <summary>Runs <paramref name="action"/> and returns what <c>OnExecute</c> received for it.</summary>
    private async Task<List<string>> Received(Func<Task> action)
    {
        lock (_executed) _executed.Clear();
        await action();
        lock (_executed) return _executed.ToList();
    }

    /// <summary>Every text names the table or view, holds the value, and has no parameter name left in it.</summary>
    private async Task ShouldSubstitute(string kind, string value, Func<Task> action)
    {
        var texts = (await Received(action)).Where(t => t.Contains("T871", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var text in texts) Output.WriteLine($"{kind}: {text}");
        texts.Should().NotBeEmpty($"{kind} runs a statement");
        texts.Should().OnlyContain(t => !Placeholder.IsMatch(t), $"{kind} passes OnExecute the text with the values written in");
        texts.Should().Contain(t => t.Contains(value, StringComparison.Ordinal), $"{kind} passes OnExecute the value {value}");
    }

    private static string Marker() => "m871-" + Guid.NewGuid().ToString("N")[..12];

    private Func<Task> Sync(Action action) => () => { action(); return Task.CompletedTask; };

    private IReadOnlyCollection<Conditions.Condition> ViewConditions(ViewDefinition definition, Expression<Func<T871View, bool>> filter)
    {
        var view = SqlViewTranslator.Translate(definition);
        using var scope = DataBase.ScopeFieldResolver((type, property, withTableName) =>
        {
            if (type != typeof(T871View)) return null;
            return view.GetTableFields().FirstOrDefault(f => f.Property?.Name == property)?.GetSelectName(withTableName);
        });
        return DataBase.ParseConditionExpression(filter).ToList();
    }

    [Fact]
    public async Task Insert_passes_the_values()
    {
        if (_settings == null) return;
        var sync = Marker();
        var async = Marker();
        await ShouldSubstitute("sync insert", sync, Sync(() => _sync!.Create(new T871Row { Code = sync })));
        await ShouldSubstitute("async insert", async, () => _async!.CreateAsync(new T871Row { Code = async }));
    }

    [Fact]
    public async Task Read_by_id_passes_the_values()
    {
        if (_settings == null) return;
        var row = new T871Row { Code = Marker() };
        await _async!.CreateAsync(row);
        var id = row.Guid!.Value.ToString("D");
        await ShouldSubstitute("sync read by id", id, Sync(() => _sync!.Read(row.Guid.Value).Should().NotBeNull()));
        await ShouldSubstitute("async read by id", id, async () => (await _async.ReadAsync(row.Guid.Value)).Should().NotBeNull());
    }

    [Fact]
    public async Task Read_by_filter_passes_the_values()
    {
        if (_settings == null) return;
        var code = Marker();
        await _async!.CreateAsync(new T871Row { Code = code });
        await ShouldSubstitute("sync read by filter", code, Sync(() => _sync!.Read(x => x.Code == code).Should().HaveCount(1)));
        await ShouldSubstitute("async read by filter", code, async () => (await _async.ReadAsync(x => x.Code == code)).Should().HaveCount(1));
        await ShouldSubstitute("sync read with paging", code, Sync(() => _sync!.Read(x => x.Code == code, null, 5, 0).Should().HaveCount(1)));
        await ShouldSubstitute("async read with paging", code, async () => (await _async.ReadAsync(x => x.Code == code, null, 5, 0)).Should().HaveCount(1));
    }

    [Fact]
    public async Task Count_passes_the_values()
    {
        if (_settings == null) return;
        var code = Marker();
        await _async!.CreateAsync(new T871Row { Code = code });
        await ShouldSubstitute("sync count", code, Sync(() => _sync!.Count(x => x.Code == code).Should().Be(1)));
        await ShouldSubstitute("async count", code, async () => (await _async.CountAsync(x => x.Code == code)).Should().Be(1));
    }

    [Fact]
    public async Task Update_passes_the_values()
    {
        if (_settings == null) return;
        var row = new T871Row { Code = Marker() };
        await _async!.CreateAsync(row);
        var sync = Marker();
        var async = Marker();
        row.Code = sync;
        await ShouldSubstitute("sync update", sync, Sync(() => _sync!.Update(row)));
        row.Code = async;
        await ShouldSubstitute("async update", async, () => _async.UpdateAsync(row));
    }

    [Fact]
    public async Task Filter_update_passes_the_values()
    {
        if (_settings == null) return;
        var code = Marker();
        await _async!.CreateAsync(new T871Row { Code = code });
        await ShouldSubstitute("sync filter update (set)", code, Sync(() => _sync!.Update(x => x.Code == code, new PropertyUpdate<T871Row>().Set(x => x.Hits, 7))));
        await ShouldSubstitute("async filter update (set)", code, () => _async.UpdateAsync(x => x.Code == code, new PropertyUpdate<T871Row>().Set(x => x.Hits, 8)));
        await ShouldSubstitute("sync filter update (action)", code, Sync(() => _sync!.Update(x => x.Code == code, r => r.Hits = 9)));
        await ShouldSubstitute("async filter update (action)", code, () => _async.UpdateAsync(x => x.Code == code, r => r.Hits = 10));
    }

    [Fact]
    public async Task Delete_passes_the_values()
    {
        if (_settings == null) return;
        var sync = new T871Row { Code = Marker() };
        var async = new T871Row { Code = Marker() };
        await _async!.CreateAsync(sync);
        await _async.CreateAsync(async);
        await ShouldSubstitute("sync delete", sync.Guid!.Value.ToString("D"), Sync(() => _sync!.Delete(sync)));
        await ShouldSubstitute("async delete", async.Guid!.Value.ToString("D"), () => _async.DeleteAsync(async));
    }

    [Fact]
    public async Task Filter_delete_passes_the_values()
    {
        if (_settings == null) return;
        var sync = Marker();
        var async = Marker();
        await _async!.CreateAsync(new T871Row { Code = sync });
        await _async.CreateAsync(new T871Row { Code = async });
        await ShouldSubstitute("sync filter delete", sync, Sync(() => _sync!.Delete(x => x.Code == sync)));
        await ShouldSubstitute("async filter delete", async, () => _async.DeleteAsync(x => x.Code == async));
    }

    [Theory]
    [InlineData(PortableMode.OnTheFly)]
    [InlineData(PortableMode.Persistent)]
    public async Task View_read_and_count_pass_the_values(PortableMode mode)
    {
        if (_settings == null) return;
        var code = Marker();
        await _async!.CreateAsync(new T871Row { Code = code, Hits = 3 });
        var definition = Definition(mode);
        var connector = _connectors[0];
        var store = new SqlViewStore<T871View>(connector, definition);

        await ShouldSubstitute($"async view read ({mode})", code, async () => (await store.QueryAsync(v => v.Code == code)).Should().HaveCount(1));
        await ShouldSubstitute($"async view count ({mode})", code, async () => (await store.CountAsync(v => v.Code == code)).Should().Be(1));

        var view = SqlViewTranslator.Translate(definition);
        var conditions = ViewConditions(definition, v => v.Code == code);
        await ShouldSubstitute($"sync view read ({mode})", code, Sync(() => connector.Select(view, (_, _) => new object(), conditions, null, 5, 0).Should().HaveCount(1)));
        await ShouldSubstitute($"sync view count ({mode})", code, Sync(() => connector.SelectCount(view, conditions).Should().Be(1)));
    }
}

public sealed class SqliteTask871OnExecuteTextTests : Task871OnExecuteTextTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t871-" + Guid.NewGuid().ToString("N"));
    public SqliteTask871OnExecuteTextTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string? HostVariable => null;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t871.db");
    protected override IBulkStore<T871Row> SyncStore(PasswordSettings settings) { var s = new SQLiteStore<T871Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T871Row> AsyncStore(PasswordSettings settings) { var s = new AsyncSQLiteStore<T871Row>(); s.SetSettings(settings); return s; }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask871OnExecuteTextTests : Task871OnExecuteTextTestsBase
{
    public PostgreSqlTask871OnExecuteTextTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        LiveDatabase.Name("PG"),
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IBulkStore<T871Row> SyncStore(PasswordSettings settings) { var s = new PostgreSQLStore<T871Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T871Row> AsyncStore(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T871Row>(); s.SetSettings(settings); return s; }
}

public sealed class MySqlTask871OnExecuteTextTests : Task871OnExecuteTextTestsBase
{
    public MySqlTask871OnExecuteTextTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        LiveDatabase.Name("MYSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IBulkStore<T871Row> SyncStore(PasswordSettings settings) { var s = new MySQLStore<T871Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T871Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMySQLStore<T871Row>(); s.SetSettings(settings); return s; }
}

public sealed class MSSqlTask871OnExecuteTextTests : Task871OnExecuteTextTestsBase
{
    public MSSqlTask871OnExecuteTextTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        LiveDatabase.Name("MSSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IBulkStore<T871Row> SyncStore(PasswordSettings settings) { var s = new MSSqlStore<T871Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T871Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMSSqlStore<T871Row>(); s.SetSettings(settings); return s; }
}
