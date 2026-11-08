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
/// TASK-858 — a view filter with a value fragment (arithmetic, <c>COALESCE</c>, <c>CASE</c>, <c>Math.Round</c>, column
/// against column) on a <see cref="SqlViewStore{TView}"/>.
/// <para>
/// OnTheFly, measured before on SQLite, PostgreSQL, MySQL and SQL Server: every such filter threw "Cannot translate operand
/// 'v.Amount'". The provider re-rendered the fragment when it built the statement (TASK-856, every provider since TASK-857)
/// and resolved the view's columns again, outside the parse's <c>ScopeFieldResolver</c>. PostgreSQL ran these filters
/// before TASK-857. The re-render now reads what the parse resolved.
/// </para>
/// <para>
/// Persistent, measured before: the view's columns are its properties, the fragment's are the source's, and the view
/// resolver dropped the provider's rendering, so each provider failed with an error naming something else ("no such
/// column: T858VOrders.Amount", 42P01, "'BIRKO_ROUND_TOEVEN' is not a recognized built-in function name"). A computed
/// filter is now refused by name before anything runs; a plain or column-to-column filter runs as before.
/// </para>
/// </summary>
[Collection("T858V")]
public abstract class Task858ViewFragmentFilterTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task858ViewFragmentFilterTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    public class T858VOrder : AbstractModel
    {
        public string? Code { get; set; }
        public decimal Amount { get; set; }
        public decimal? Optional { get; set; }
        public int Hits { get; set; }
    }

    public class T858VView
    {
        public string? Code { get; set; }
        public decimal Amount { get; set; }
        public decimal? Optional { get; set; }
        public int Hits { get; set; }
    }

    private sealed class OrderMapping : IModelMapping<T858VOrder>
    {
        public void Configure(ModelMap<T858VOrder> map)
        {
            map.ToTable("T858VOrders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Code);
            map.Property(x => x.Amount).HasPrecision(18).HasScale(4);
            map.Property(x => x.Optional).HasPrecision(18).HasScale(4);
            map.Property(x => x.Hits);
        }
    }

    private static ViewDefinition Definition(PortableMode mode) => new ViewDefinitionBuilder<T858VView>()
        .HasName("T858VView").HasQueryMode(mode)
        .From<T858VOrder>()
        .Select<T858VOrder, string>(o => o.Code!, v => v.Code)
        .Select<T858VOrder, decimal>(o => o.Amount, v => v.Amount)
        .Select<T858VOrder, decimal?>(o => o.Optional, v => v.Optional)
        .Select<T858VOrder, int>(o => o.Hits, v => v.Hits)
        .Build();

    private static readonly decimal[] Small = { 10.10m, 5.25m };

    /// <summary>Filters that compute over the row: run OnTheFly, refused on a persistent view.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<T858VView, bool>>> Computed =
        new Dictionary<string, Expression<Func<T858VView, bool>>>
        {
            ["Amount + 0 == 10.10"] = v => v.Amount + 0m == 10.10m,
            ["Small.Contains(Amount + 0)"] = v => Small.Contains(v.Amount + 0m),
            ["Amount + 0 < 6"] = v => v.Amount + 0m < 6m,
            ["(Optional ?? 0) < 8"] = v => (v.Optional ?? 0m) < 8m,
            ["CASE < 8"] = v => (v.Amount > 6m ? v.Amount : 0m) < 8m,
            ["Round(Amount, 1, ToEven) == 10.2"] = v => Math.Round(v.Amount, 1, MidpointRounding.ToEven) == 10.2m,
            ["Amount + 0 == Amount"] = v => v.Amount + 0m == v.Amount,
            ["Hits + 1 == 8"] = v => v.Hits + 1 == 8,
            ["Optional + 1 != 9.5 (nullable)"] = v => v.Optional + 1m != 9.5m,
            ["Amount < Optional + 1"] = v => v.Amount < v.Optional + 1m,
        };

    /// <summary>Filters over plain view columns: run on both paths.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<T858VView, bool>>> Plain =
        new Dictionary<string, Expression<Func<T858VView, bool>>>
        {
            ["Amount > 9.5"] = v => v.Amount > 9.5m,
            ["Amount == Optional"] = v => v.Amount == v.Optional,
            ["Amount < Optional"] = v => v.Amount < v.Optional,
            ["Amount != Optional"] = v => v.Amount != v.Optional,
            ["Optional != 5.5"] = v => v.Optional != 5.5m,
        };

    public static IEnumerable<object[]> ComputedNames => Computed.Keys.Select(k => new object[] { k });
    public static IEnumerable<object[]> PlainNames => Plain.Keys.Select(k => new object[] { k });

    private AbstractConnector? _connector;
    private readonly List<T858VView> _seed = new();
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
        registry.Register(new OrderMapping());
        registry.ApplyToDatabase();

        var connector = Connector();
        _connector = connector;
        await CleanAsync(connector);
        connector.CreateTable(new[] { typeof(T858VOrder) });
        var code = 0;
        // Two values a double cannot tell apart are absent here on purpose: SQLite's exact comparison is TASK-856's.
        foreach (var amount in new[] { 10.10m, 10.1m, 5.25m, 0.30m, 7m, 10.14m, 0m, 10.15m, 9.5m })
        {
            var order = new T858VOrder
            {
                Guid = Guid.NewGuid(),
                Code = "r" + code++,
                Amount = amount,
                Optional = amount == 7m ? null : amount == 0m ? 0m : amount * 2m - 10m,
                Hits = (int)amount,
            };
            await ((AbstractAsyncConnector)connector).InsertAsync(typeof(T858VOrder), order);
            _seed.Add(new T858VView { Code = order.Code, Amount = order.Amount, Optional = order.Optional, Hits = order.Hits });
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
        try { await new SqlViewManager(connector).DropAsync("T858VView"); } catch { }
        try { connector.DropTable(new[] { typeof(T858VOrder) }); } catch { }
    }

    private List<string?> Expected(Expression<Func<T858VView, bool>> filter)
        => _seed.Where(filter.Compile()).Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();

    private async Task Matches(PortableMode mode, string shape, Expression<Func<T858VView, bool>> filter)
    {
        var store = new SqlViewStore<T858VView>(_connector!, Definition(mode));
        var expected = Expected(filter);
        (await store.QueryAsync(filter)).Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal).Should()
            .Equal(expected, $"{mode} '{shape}' must match the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count, $"{mode} '{shape}' must count the rows C# matches");
    }

    [Theory]
    [MemberData(nameof(ComputedNames))]
    public async Task An_OnTheFly_view_filter_that_computes_over_the_row_matches_what_CSharp_matches(string shape)
    {
        if (_connector == null) return;
        await Matches(PortableMode.OnTheFly, shape, Computed[shape]);
    }

    [Theory]
    [MemberData(nameof(PlainNames))]
    public async Task A_view_filter_over_plain_columns_matches_what_CSharp_matches_on_both_paths(string shape)
    {
        if (_connector == null) return;
        await Matches(PortableMode.OnTheFly, shape, Plain[shape]);
        await Matches(PortableMode.Persistent, shape, Plain[shape]);
    }

    [Theory]
    [MemberData(nameof(ComputedNames))]
    public async Task A_persistent_view_refuses_a_filter_that_computes_over_the_row_by_name_before_it_runs(string shape)
    {
        if (_connector == null) return;
        var store = new SqlViewStore<T858VView>(_connector, Definition(PortableMode.Persistent));
        _executed.Clear();

        var read = () => store.QueryAsync(Computed[shape]);
        (await read.Should().ThrowAsync<NotSupportedException>($"'{shape}' reads source columns the view does not have"))
            .WithMessage("View 'T858VView': the filter computes *over the view's source columns*");
        var count = () => store.CountAsync(Computed[shape]);
        await count.Should().ThrowAsync<NotSupportedException>();
        _executed.Should().NotContain(text => text.Contains("SELECT", StringComparison.OrdinalIgnoreCase), "nothing ran");
    }
}

public sealed class SqliteTask858ViewFragmentFilterTests : Task858ViewFragmentFilterTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t858v-{Guid.NewGuid():N}.db");
    public SqliteTask858ViewFragmentFilterTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));
}

public sealed class PostgreSqlTask858ViewFragmentFilterTests : Task858ViewFragmentFilterTestsBase
{
    public PostgreSqlTask858ViewFragmentFilterTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => new PostgreSQLConnector(new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 });
}

public sealed class MySqlTask858ViewFragmentFilterTests : Task858ViewFragmentFilterTestsBase
{
    public MySqlTask858ViewFragmentFilterTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => new MySQLConnector(new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 });
}

public sealed class MSSqlTask858ViewFragmentFilterTests : Task858ViewFragmentFilterTestsBase
{
    public MSSqlTask858ViewFragmentFilterTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => new MSSqlConnector(new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true });
}
