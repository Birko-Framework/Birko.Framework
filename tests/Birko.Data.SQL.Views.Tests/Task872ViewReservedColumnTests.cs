using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using PortableMode = Birko.Data.Views.ViewQueryMode;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-872 — a persistent SQL view whose own column (a view property) is a name the provider rejects is refused by name,
/// before any SQL is sent: when Birko creates it and when it is read, sync or async, select or count. The source table is
/// clean and Birko built it; only the view property is reserved.
/// <para>
/// Before, measured live: a persistent view's columns are emitted bare (<c>AS User</c>, <c>SELECT User, …</c>,
/// <c>WHERE User = …</c>). PostgreSQL and SQLite created the view and every read returned the value function (the
/// session user, the current date) in place of the column: a filter on the function's value matched 2 of 2 rows that C#
/// matched none of, and a filter on a stored value matched none. MySQL and SQL Server refused the DDL, and a view made
/// outside Birko read the function on every provider. An on-the-fly read names source columns qualified and is not
/// affected; it keeps working.
/// </para>
/// </summary>
[Collection("T872V")]
public abstract class Task872ViewReservedColumnTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task872ViewReservedColumnTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    /// <summary>The provider's value function used as a view property: User, CURRENT_USER or CURRENT_DATE.</summary>
    protected abstract string PropertyName { get; }

    /// <summary>The provider's name as the refusal shows it.</summary>
    protected abstract string ProviderName { get; }

    [Table("T872VSource")]
    public class T872VSource : AbstractModel
    {
        public string? Login { get; set; }
        public int Amount { get; set; }
    }

    public class T872VView
    {
        public string? User { get; set; }
        public string? CURRENT_DATE { get; set; }
        public string? CURRENT_USER { get; set; }
        public int Amount { get; set; }
    }

    private const string ViewName = "T872VView";

    private ViewDefinition Definition(PortableMode mode)
    {
        var v = Expression.Parameter(typeof(T872VView), "v");
        var target = Expression.Lambda<Func<T872VView, string>>(Expression.Property(v, PropertyName), v);
        return new ViewDefinitionBuilder<T872VView>()
            .HasName(ViewName).HasQueryMode(mode)
            .From<T872VSource>()
            .Select<T872VSource, string>(s => s.Login!, target)
            .Select<T872VSource, int>(s => s.Amount, x => x.Amount)
            .Build();
    }

    private Expression<Func<T872VView, bool>> Equal(string? value)
    {
        var v = Expression.Parameter(typeof(T872VView), "v");
        return Expression.Lambda<Func<T872VView, bool>>(
            Expression.Equal(Expression.Property(v, PropertyName), Expression.Constant(value, typeof(string))), v);
    }

    private string? Read(T872VView row) => (string?)typeof(T872VView).GetProperty(PropertyName)!.GetValue(row);

    private AbstractConnector? _connector;
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
        var connector = Connector();
        _connector = connector;
        await CleanAsync(connector);
        connector.CreateTable(new[] { typeof(T872VSource) });
        connector.Insert(typeof(T872VSource), new T872VSource { Guid = Guid.NewGuid(), Login = "a", Amount = 1 });
        connector.Insert(typeof(T872VSource), new T872VSource { Guid = Guid.NewGuid(), Login = "b", Amount = 2 });
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
        try { await new SqlViewManager(connector).DropAsync(ViewName); } catch { }
        try { connector.DropTable(new[] { typeof(T872VSource) }); } catch { }
    }

    private void Run(string sql) => _connector!.DoCommand(c => c.CommandText = sql, c => c.ExecuteNonQuery());

    private void ShouldNameTheColumn(NotSupportedException ex)
        => ex.Message.Should().Contain($"\"{PropertyName}\"").And.Contain($"\"{ViewName}\"").And.Contain(ProviderName)
            .And.Contain("reserved word").And.Contain("Rename the view property");

    [Fact]
    public async Task Creating_a_persistent_view_with_a_rejected_column_is_refused_before_the_DDL()
    {
        if (_connector == null) return;
        _executed.Clear();

        var ensure = () => new SqlViewManager(_connector).EnsureAsync(Definition(PortableMode.Persistent));

        ShouldNameTheColumn((await ensure.Should().ThrowAsync<NotSupportedException>()).Which);
        FluentActions.Invoking(() => _connector.CreateView(SqlViewTranslator.Translate(Definition(PortableMode.Persistent))))
            .Should().Throw<NotSupportedException>();
        _executed.Should().NotContain(t => t.Contains("CREATE", StringComparison.OrdinalIgnoreCase), "no view was created");
    }

    [Fact]
    public async Task Reading_a_persistent_view_with_a_rejected_column_is_refused_before_the_SELECT()
    {
        if (_connector == null) return;
        // A view made outside Birko, its column quoted: every provider accepts it, and Birko's bare read used to return
        // the value function in place of the column.
        var source = _connector.TableIdentifier("T872VSource");
        Run($"CREATE VIEW {_connector.QuoteIdentifier(ViewName)} AS SELECT {source}.Login AS {_connector.QuoteIdentifier(PropertyName)}, "
            + $"{source}.Amount AS Amount FROM {source}");
        var store = new SqlViewStore<T872VView>(_connector, Definition(PortableMode.Persistent));
        _executed.Clear();

        ShouldNameTheColumn((await FluentActions.Awaiting(() => store.QueryAsync()).Should().ThrowAsync<NotSupportedException>()).Which);
        await FluentActions.Awaiting(() => store.QueryAsync(Equal("a"))).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => store.QueryFirstAsync(Equal("a"))).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => store.CountAsync(Equal("a"))).Should().ThrowAsync<NotSupportedException>();
        var view = SqlViewTranslator.Translate(Definition(PortableMode.Persistent));
        FluentActions.Invoking(() => _connector.Select(view, null, (IEnumerable<Birko.Data.SQL.Conditions.Condition>?)null).ToList()).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => _connector.SelectCount(view)).Should().Throw<NotSupportedException>();
        _executed.Should().NotContain(t => t.Contains(ViewName, StringComparison.OrdinalIgnoreCase), "nothing read the view");
    }

    [Fact]
    public async Task The_same_view_read_on_the_fly_answers_what_CSharp_answers()
    {
        if (_connector == null) return;
        var store = new SqlViewStore<T872VView>(_connector, Definition(PortableMode.OnTheFly));

        (await store.QueryAsync()).Select(Read).OrderBy(x => x).Should().Equal("a", "b");
        (await store.QueryAsync(Equal("a"))).Should().ContainSingle();
        (await store.CountAsync(Equal("b"))).Should().Be(1);
    }
}

public sealed class SqliteTask872ViewReservedColumnTests : Task872ViewReservedColumnTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t872v-{Guid.NewGuid():N}.db");
    public SqliteTask872ViewReservedColumnTests(ITestOutputHelper output) : base(output) { }
    protected override string ProviderName => "SQLite";
    protected override string? HostVariable => null;
    protected override string PropertyName => "CURRENT_DATE";
    protected override AbstractConnector Connector() => new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));
}

public sealed class PostgreSqlTask872ViewReservedColumnTests : Task872ViewReservedColumnTestsBase
{
    public PostgreSqlTask872ViewReservedColumnTests(ITestOutputHelper output) : base(output) { }
    protected override string ProviderName => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override string PropertyName => "User";
    protected override AbstractConnector Connector() => new PostgreSQLConnector(new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        LiveDatabase.Name("PG"),
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 });
}

public sealed class MySqlTask872ViewReservedColumnTests : Task872ViewReservedColumnTestsBase
{
    public MySqlTask872ViewReservedColumnTests(ITestOutputHelper output) : base(output) { }
    protected override string ProviderName => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override string PropertyName => "CURRENT_USER";
    protected override AbstractConnector Connector() => new MySQLConnector(new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        LiveDatabase.Name("MYSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 });
}

public sealed class MSSqlTask872ViewReservedColumnTests : Task872ViewReservedColumnTestsBase
{
    public MSSqlTask872ViewReservedColumnTests(ITestOutputHelper output) : base(output) { }
    protected override string ProviderName => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override string PropertyName => "User";
    protected override AbstractConnector Connector() => new MSSqlConnector(new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        LiveDatabase.Name("MSSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true });
}
