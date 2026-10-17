using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Views;
using Birko.Data.Views;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using PortableMode = Birko.Data.Views.ViewQueryMode;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-639, live: a filter on a view property is applied to a Persistent or Auto view, whose columns are the view
/// properties, not the source columns the OnTheFly SELECT reads. One view renames a single source column; the other joins
/// and groups, so its source column is table-qualified.
/// </summary>
[Collection("T639")]
public abstract class Task639PersistentViewFilterLiveTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task639PersistentViewFilterLiveTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    public class T639Person : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class T639Order : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
    }

    public class T639NamesView
    {
        public string PersonName { get; set; } = string.Empty;
    }

    public class T639TotalsView
    {
        public string PersonName { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }

    private sealed class PersonMapping : IModelMapping<T639Person>
    {
        public void Configure(ModelMap<T639Person> map)
        {
            map.ToTable("T639Persons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private sealed class OrderMapping : IModelMapping<T639Order>
    {
        public void Configure(ModelMap<T639Order> map)
        {
            map.ToTable("T639Orders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Amount);
        }
    }

    /// <summary>The attribute-declared path: <c>SelectView(Type, …)</c> funnels into the same Select.</summary>
    [Birko.Data.SQL.Attributes.View(typeof(T639Order), typeof(T639Person), nameof(T639Order.PersonId), nameof(T639Person.Guid), name: "T639Attr", QueryMode = Birko.Data.SQL.ViewQueryMode.Persistent)]
    public class T639AttrView
    {
        [Birko.Data.SQL.Attributes.ViewField(typeof(T639Person), nameof(T639Person.Name))]
        public string? PersonName { get; set; }

        [Birko.Data.SQL.Attributes.ViewField(typeof(T639Order), nameof(T639Order.Amount))]
        public decimal Amount { get; set; }
    }

    private AbstractConnector? _connector;

    private bool Live()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private static ViewDefinition Names(PortableMode mode) => new ViewDefinitionBuilder<T639NamesView>()
        .HasName("T639Names").HasQueryMode(mode)
        .From<T639Person>()
        .Select<T639Person, string>(p => p.Name!, v => v.PersonName)
        .Build();

    private static ViewDefinition Totals(PortableMode mode) => new ViewDefinitionBuilder<T639TotalsView>()
        .HasName("T639Totals").HasQueryMode(mode)
        .From<T639Order>()
        .Join<T639Order, T639Person, Guid?>(o => o.PersonId, p => p.Guid)
        .Select<T639Person, string>(p => p.Name!, v => v.PersonName)
        .GroupBy<T639Person, string>(p => p.Name!)
        .Sum<T639Order, decimal>(o => o.Amount, v => v.Total)
        .Build();

    public async Task InitializeAsync()
    {
        if (!Live()) return;
        var registry = new ModelMapRegistry();
        registry.Register(new PersonMapping());
        registry.Register(new OrderMapping());
        registry.ApplyToDatabase();

        var connector = Connector();
        _connector = connector;
        await CleanAsync(connector);
        connector.CreateTable(new[] { typeof(T639Person), typeof(T639Order) });
        var async = (AbstractAsyncConnector)connector;
        for (var i = 1; i <= 3; i++)
        {
            var person = new T639Person { Guid = Guid.NewGuid(), Name = "p" + i };
            await async.InsertAsync(typeof(T639Person), person);
            for (var j = 1; j <= i; j++)
                await async.InsertAsync(typeof(T639Order), new T639Order { Guid = Guid.NewGuid(), PersonId = person.Guid!.Value, Amount = 10 * j });
        }
        var views = new SqlViewManager(connector);
        await views.EnsureAsync(Names(PortableMode.Persistent));
        await views.EnsureAsync(Totals(PortableMode.Persistent));
        connector.CreateView(typeof(T639AttrView));
    }

    public async Task DisposeAsync()
    {
        if (_connector != null) await CleanAsync(_connector);
    }

    private static async Task CleanAsync(AbstractConnector connector)
    {
        var views = new SqlViewManager(connector);
        try { await views.DropAsync("T639Totals"); } catch { }
        try { await views.DropAsync("T639Names"); } catch { }
        try { await views.DropAsync("T639Attr"); } catch { }
        try { connector.DropTable(new[] { typeof(T639Order), typeof(T639Person) }); } catch { }
    }

    [Theory]
    [InlineData(PortableMode.OnTheFly)]
    [InlineData(PortableMode.Persistent)]
    [InlineData(PortableMode.Auto)]
    public async Task A_filter_on_a_renamed_property_of_a_single_source_view(PortableMode mode)
    {
        if (_connector == null) return;
        var store = new SqlViewStore<T639NamesView>(_connector, Names(mode));

        (await store.QueryAsync(v => v.PersonName == "p2")).Select(v => v.PersonName).Should().Equal("p2");
        (await store.CountAsync(v => v.PersonName != "p2")).Should().Be(2);
        (await store.QueryFirstAsync(v => v.PersonName == "p3"))!.PersonName.Should().Be("p3");
    }

    [Theory]
    [InlineData(PortableMode.OnTheFly)]
    [InlineData(PortableMode.Persistent)]
    [InlineData(PortableMode.Auto)]
    public async Task A_filter_on_a_joined_and_grouped_view(PortableMode mode)
    {
        if (_connector == null) return;
        var store = new SqlViewStore<T639TotalsView>(_connector, Totals(mode));

        (await store.QueryAsync(v => v.PersonName == "p2")).Should().ContainSingle().Which.Total.Should().Be(30m);
        (await store.CountAsync(v => v.PersonName == "p3")).Should().Be(1);
    }

    [Fact]
    public void A_filter_on_an_attribute_declared_persistent_view()
    {
        if (_connector == null) return;
        System.Linq.Expressions.Expression<Func<T639AttrView, bool>> p2 = v => v.PersonName == "p2";

        _connector.SelectView(typeof(T639AttrView), p2).Cast<T639AttrView>().Select(v => v.Amount).Should().BeEquivalentTo(new[] { 10m, 20m });
        _connector.SelectCountView(typeof(T639AttrView), p2).Should().Be(2);
    }
}

public sealed class SqliteTask639PersistentViewFilterLiveTests : Task639PersistentViewFilterLiveTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t639-{Guid.NewGuid():N}.db");
    public SqliteTask639PersistentViewFilterLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));
}

public sealed class PostgreSqlTask639PersistentViewFilterLiveTests : Task639PersistentViewFilterLiveTestsBase
{
    public PostgreSqlTask639PersistentViewFilterLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => new PostgreSQLConnector(new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 });
}

public sealed class MySqlTask639PersistentViewFilterLiveTests : Task639PersistentViewFilterLiveTestsBase
{
    public MySqlTask639PersistentViewFilterLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => new MySQLConnector(new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 });
}

public sealed class MSSqlTask639PersistentViewFilterLiveTests : Task639PersistentViewFilterLiveTestsBase
{
    public MSSqlTask639PersistentViewFilterLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => new MSSqlConnector(new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true });
}
