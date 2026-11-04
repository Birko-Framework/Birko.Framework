using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-717, live. Measured before the fix: SQLite's <c>CreateView</c> was <c>CREATE VIEW IF NOT EXISTS</c>, so a second
/// definition under one name was silently kept as the first; a bool constant in a view's join rendered <c>TRUE</c>, which
/// SQL Server refuses; the base <c>ViewExistsAsync</c> (PostgreSQL and SQL Server have no override) probed with a SELECT
/// inside a catch-all, so a same-named table answered true and a cancelled token answered false.
/// </summary>
[Collection("T717")]
public abstract class Task717ViewDdlLiveTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task717ViewDdlLiveTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    public class T717Person : AbstractModel
    {
        public string? Name { get; set; }
        public bool Active { get; set; }
    }

    public class T717Order : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class PersonMapping : IModelMapping<T717Person>
    {
        public void Configure(ModelMap<T717Person> map)
        {
            map.ToTable("T717Persons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name);
            map.Property(x => x.Active);
        }
    }

    private sealed class OrderMapping : IModelMapping<T717Order>
    {
        public void Configure(ModelMap<T717Order> map)
        {
            map.ToTable("T717Orders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Amount);
        }
    }

    [Birko.Data.SQL.Attributes.View(typeof(T717Order), typeof(T717Person), nameof(T717Order.PersonId), nameof(T717Person.Guid), name: "T717Narrow")]
    public class T717NarrowView
    {
        [Birko.Data.SQL.Attributes.ViewField(typeof(T717Person), nameof(T717Person.Name))]
        public string? PersonName { get; set; }
    }

    [Birko.Data.SQL.Attributes.View(typeof(T717Order), typeof(T717Person), nameof(T717Order.PersonId), nameof(T717Person.Guid), name: "T717Wide")]
    public class T717WideView
    {
        [Birko.Data.SQL.Attributes.ViewField(typeof(T717Order), nameof(T717Order.Guid))]
        public Guid OrderId { get; set; }

        [Birko.Data.SQL.Attributes.ViewField(typeof(T717Person), nameof(T717Person.Name))]
        public string? PersonName { get; set; }

        [Birko.Data.SQL.Attributes.ViewField(typeof(T717Order), nameof(T717Order.Amount))]
        public decimal Amount { get; set; }
    }

    protected AbstractConnector? Db;

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
        registry.Register(new PersonMapping());
        registry.Register(new OrderMapping());
        registry.ApplyToDatabase();

        var connector = Connector();
        Db = connector;
        Clean(connector);
        connector.CreateTable(new[] { typeof(T717Person), typeof(T717Order) });
        var async = (AbstractAsyncConnector)connector;
        for (var i = 1; i <= 3; i++)
        {
            var person = new T717Person { Guid = Guid.NewGuid(), Name = "p" + i, Active = i != 2 };
            await async.InsertAsync(typeof(T717Person), person);
            await async.InsertAsync(typeof(T717Order), new T717Order { Guid = Guid.NewGuid(), PersonId = person.Guid!.Value, Amount = 10 * i });
        }
    }

    public Task DisposeAsync()
    {
        if (Db != null) Clean(Db);
        return Task.CompletedTask;
    }

    protected virtual void Clean(AbstractConnector connector)
    {
        foreach (var view in new[] { "T717Same", "T717Active", "T717Narrow", "T717Wide", "T717SameAsync" })
            try { connector.DropView(view); } catch { }
        try { connector.DropTable(new[] { typeof(T717Order), typeof(T717Person) }); } catch { }
        try { Execute(connector, "DROP TABLE " + connector.QuoteIdentifier("T717Fake")); } catch { }
    }

    protected static void Execute(AbstractConnector connector, string sql)
    {
        using var connection = connector.CreateConnection((PasswordSettings)connector.Settings);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    protected static List<string> Columns(AbstractConnector connector, string view)
    {
        using var connection = connector.CreateConnection((PasswordSettings)connector.Settings);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM " + connector.QuoteIdentifier(view) + " WHERE 1=0";
        using var reader = command.ExecuteReader();
        return Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
    }

    protected static long Count(AbstractConnector connector, string view)
    {
        using var connection = connector.CreateConnection((PasswordSettings)connector.Settings);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + connector.QuoteIdentifier(view);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>The narrow view's columns, its join also requiring <c>Amount = 10</c> (one row of three).</summary>
    private static Tables.View NarrowFiltered(string name)
    {
        var loaded = DataBase.LoadView(typeof(T717NarrowView))!;
        var joins = loaded.Join!.Select(j => new Join(j.Left, j.Right, j.JoinType,
            (j.Conditions ?? Enumerable.Empty<Condition>()).Append(Condition.AndValue("T717Orders.Amount", 10)).ToList())).ToList();
        return new Tables.View(loaded.Tables, joins, name);
    }

    [Fact]
    public void CreateView_replaces_an_existing_definition()
    {
        if (Db == null) return;

        Db.CreateView(typeof(T717NarrowView), "T717Same");
        Db.CreateView(NarrowFiltered("T717Same"));

        Count(Db, "T717Same").Should().Be(1, "the second definition filters to one order");
    }

    [Fact]
    public async Task CreateViewAsync_replaces_an_existing_definition()
    {
        if (Db == null) return;
        var async = (AbstractAsyncConnector)Db;

        await async.CreateViewAsync(typeof(T717NarrowView), "T717SameAsync");
        await async.CreateViewAsync(NarrowFiltered("T717SameAsync"));

        Count(Db, "T717SameAsync").Should().Be(1, "the second definition filters to one order");
    }

    /// <summary>
    /// A definition with other columns: SQLite, MySQL and SQL Server replace it; PostgreSQL refuses it in the open
    /// (42P16, <c>CREATE OR REPLACE VIEW</c> keeps the column list), so <c>RecreateView</c> is the way there.
    /// </summary>
    [Fact]
    public void A_definition_with_other_columns_replaces_or_is_refused()
    {
        if (Db == null) return;

        Db.CreateView(typeof(T717NarrowView), "T717Same");
        var replace = () => Db.CreateView(typeof(T717WideView), "T717Same");

        if (Db is PostgreSQLConnector)
        {
            replace.Should().Throw<Exception>().Where(e => e.Message.Contains("42P16"));
            Db.RecreateView(typeof(T717WideView), "T717Same");
        }
        else
        {
            replace();
        }
        Columns(Db, "T717Same").Select(c => c.ToLowerInvariant()).Should().Contain("amount");
    }

    [Fact]
    public void A_bool_constant_in_a_join_is_accepted()
    {
        if (Db == null) return;
        var loaded = DataBase.LoadView(typeof(T717NarrowView))!;
        var joins = loaded.Join!.Select(j => new Join(j.Left, j.Right, j.JoinType,
            (j.Conditions ?? Enumerable.Empty<Condition>()).Append(Condition.AndValue("T717Persons.Active", true)).ToList())).ToList();
        var view = new Tables.View(loaded.Tables, joins, "T717Active");

        Db.CreateView(view);

        Count(Db, "T717Active").Should().Be(2, "p1 and p3 are active");
    }

    [Fact]
    public async Task ViewExistsAsync_is_false_for_a_table_of_that_name()
    {
        if (Db == null) return;
        Execute(Db, "CREATE TABLE " + Db.QuoteIdentifier("T717Fake") + " (x INT)");

        (await ((AbstractAsyncConnector)Db).ViewExistsAsync("T717Fake")).Should().BeFalse();
        Db.ViewExists("T717Fake").Should().BeFalse();
    }

    [Fact]
    public async Task ViewExistsAsync_honours_a_cancelled_token()
    {
        if (Db == null) return;
        Db.CreateView(typeof(T717NarrowView));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await FluentActions.Awaiting(() => ((AbstractAsyncConnector)Db).ViewExistsAsync("T717Narrow", cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        (await ((AbstractAsyncConnector)Db).ViewExistsAsync("T717Narrow")).Should().BeTrue();
    }
}

public sealed class SqliteTask717ViewDdlLiveTests : Task717ViewDdlLiveTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t717-{Guid.NewGuid():N}.db");
    public SqliteTask717ViewDdlLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));
}

public sealed class PostgreSqlTask717ViewDdlLiveTests : Task717ViewDdlLiveTestsBase
{
    public PostgreSqlTask717ViewDdlLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => new PostgreSQLConnector(new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 });
}

public sealed class MySqlTask717ViewDdlLiveTests : Task717ViewDdlLiveTestsBase
{
    public MySqlTask717ViewDdlLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => new MySQLConnector(new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 });
}

public sealed class MSSqlTask717ViewDdlLiveTests : Task717ViewDdlLiveTestsBase
{
    public MSSqlTask717ViewDdlLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => new MSSqlConnector(new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true });

    protected override void Clean(AbstractConnector connector)
    {
        try { connector.DropView("T717Indexed"); } catch { }
        base.Clean(connector);
    }

    /// <summary>
    /// Measured before the fix: the clustered index named the view's key as its source, <c>[T717Orders].[Guid]</c>, a
    /// column the view does not have (its column is <c>OrderId</c>), so every indexed view failed at step 2.
    /// </summary>
    [Fact]
    public void An_indexed_view_is_keyed_by_the_view_s_own_column()
    {
        if (Db == null) return;
        var mssql = (MSSqlConnector)Db;

        mssql.CreateIndexedView(typeof(T717WideView), "T717Indexed");

        mssql.IndexedViewExists("T717Indexed").Should().BeTrue();
        Count(Db, "T717Indexed").Should().Be(3);
    }
}

/// <summary>
/// TASK-717. Measured before the fix: an async read of an Auto view decided whether the view exists with the sync
/// <c>ViewExists</c>, blocking the calling thread on a database round-trip inside an async method.
/// </summary>
public sealed class Task717AsyncReadExistsTests : IDisposable
{
    private sealed class Counting(SqLiteSettings settings) : SqLiteConnector(settings)
    {
        public int Sync;
        public int Async;
        public override bool ViewExists(string viewName) { Sync++; return base.ViewExists(viewName); }
        public override Task<bool> ViewExistsAsync(string viewName, CancellationToken ct = default) { Async++; return base.ViewExistsAsync(viewName, ct); }
    }

    public class T717AutoPerson : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class T717AutoOrder : AbstractModel
    {
        public Guid PersonId { get; set; }
    }

    [Birko.Data.SQL.Attributes.View(typeof(T717AutoOrder), typeof(T717AutoPerson), nameof(T717AutoOrder.PersonId), nameof(T717AutoPerson.Guid), name: "T717Auto", QueryMode = Birko.Data.SQL.ViewQueryMode.Auto)]
    public class T717AutoView
    {
        [Birko.Data.SQL.Attributes.ViewField(typeof(T717AutoPerson), nameof(T717AutoPerson.Name))]
        public string? PersonName { get; set; }
    }

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t717a-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task An_async_Auto_read_asks_ViewExistsAsync()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new AutoPersonMapping());
        registry.Register(new AutoOrderMapping());
        registry.ApplyToDatabase();
        var connector = new Counting(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));
        connector.CreateTable(new[] { typeof(T717AutoPerson), typeof(T717AutoOrder) });
        var view = DataBase.LoadView(typeof(T717AutoView))!;

        await foreach (var _ in connector.SelectAsync(view, (_, _) => new object())) { }
        await connector.SelectCountAsync(view);

        connector.Sync.Should().Be(0);
        connector.Async.Should().Be(2);
    }

    private sealed class AutoPersonMapping : IModelMapping<T717AutoPerson>
    {
        public void Configure(ModelMap<T717AutoPerson> map) { map.ToTable("T717AutoPersons").HasPrimary(x => x.Guid); map.Property(x => x.Name); }
    }

    private sealed class AutoOrderMapping : IModelMapping<T717AutoOrder>
    {
        public void Configure(ModelMap<T717AutoOrder> map) { map.ToTable("T717AutoOrders").HasPrimary(x => x.Guid); map.Property(x => x.PersonId); }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"));
        try { File.Delete(_path); } catch { }
    }
}
