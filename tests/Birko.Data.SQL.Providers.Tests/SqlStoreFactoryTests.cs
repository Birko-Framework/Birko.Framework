using Birko.Data.Models;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.Providers.Tests;

/// <summary>
/// TASK-988 (asked for by DraCode TASK-200): one <see cref="ISqlStoreFactory"/> for any SQL provider, chosen by a
/// provider name and a connection string, so the same code runs on SQLite in tests and PostgreSQL in production. The
/// same store suite runs on SQLite here and on PostgreSQL when <c>BIRKO_PG_HOST</c> is set.
/// </summary>
public class SqlStoreFactoryTests
{
    private readonly ITestOutputHelper _output;
    public SqlStoreFactoryTests(ITestOutputHelper output) => _output = output;

    [Birko.Data.SQL.Attributes.Table("TASK988_Gadget")]
    public class Gadget : AbstractDatabaseModel
    {
        [Birko.Data.SQL.Attributes.MaxLengthField(64)]
        public string? Name { get; set; }

        public int Weight { get; set; }
    }

    private static readonly Action<SqlStoreProviders> All = p => p.AddSqLite().AddPostgreSQL().AddMySQL().AddMSSql();

    /// <summary>What DraCode does through the factory: schema, CRUD, count, bulk, aggregate, a repository.</summary>
    private static async Task ExerciseAsync(ISqlStoreFactory factory)
    {
        await factory.CreateSchemaAsync(new[] { typeof(Gadget) });
        var store = factory.GetAsyncStore<Gadget>();
        await store.DeleteAsync(_ => true);

        var a = new Gadget { Name = "a", Weight = 2 };
        await store.CreateAsync(a);
        await store.CreateAsync(new[] { new Gadget { Name = "b", Weight = 3 }, new Gadget { Name = "c", Weight = 5 } });

        (await store.CountAsync(_ => true)).Should().Be(3);
        (await store.ReadAsync(a.Guid!.Value))!.Name.Should().Be("a");

        var repository = factory.GetAsyncRepository<Gadget>();
        repository.Connector.Should().BeSameAs(factory.GetConnector(), "stores from one factory share its connector");
        (await repository.CountAsync(x => x.Weight > 2)).Should().Be(2);

        var sum = await store.AggregateAsync(new Birko.Data.Stores.AggregateQuery<Gadget>
        {
            Aggregates = [new Birko.Data.Stores.AggregateField(Birko.Data.Stores.AggregateFunction.Sum, nameof(Gadget.Weight), "total")],
        });
        Convert.ToInt64(sum.Single().Values["total"]).Should().Be(10);

        factory.GetConnector().EnsureColumns(typeof(Gadget)).Should().BeEmpty("the table was just created from this type");
        await store.DeleteAsync(_ => true);
    }

    // ── choosing a provider ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_unregistered_provider_is_refused_naming_the_registered_ones()
    {
        var act = () => SqlStoreFactory.Create("Oracle", "x", p => p.AddSqLite().AddPostgreSQL());

        act.Should().Throw<ArgumentException>().WithMessage("*'Oracle' is not registered (registered: PostgreSQL, SqLite)*");
    }

    [Fact]
    public void An_empty_connection_string_is_refused()
    {
        var act = () => SqlStoreFactory.Create("SqLite", "  ", All);

        act.Should().Throw<ArgumentException>().WithMessage("*connection string is required*SqLite*");
    }

    [Fact]
    public void A_provider_registered_twice_is_refused()
    {
        var act = () => SqlStoreFactory.Create("SqLite", "a.db", p => p.AddSqLite().AddSqLite());

        act.Should().Throw<ArgumentException>().WithMessage("*'SqLite' is already registered*");
    }

    [Fact]
    public void The_provider_name_ignores_case_and_each_builds_its_own_factory()
    {
        SqlStoreFactory.Create("postgresql", "Host=db;Database=app", All).Should().BeOfType<PostgreSQLSqlStoreFactory>();
        SqlStoreFactory.Create("MYSQL", "Server=db;Database=app", All).Should().BeOfType<MySQLSqlStoreFactory>();
        SqlStoreFactory.Create("mssql", "Server=db;Database=app", All).Should().BeOfType<MSSqlSqlStoreFactory>();
        SqlStoreFactory.Create("SqLite", Path.Combine(Path.GetTempPath(), "task988-name.db"), All).Provider.Should().Be("SqLite");
    }

    // ── SQLite ──────────────────────────────────────────────────────────────────────────────────

    private static string TempDb(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "birko-task988", Guid.NewGuid().ToString("N"), "nested");
        return Path.Combine(dir, name);
    }

    [Fact]
    public async Task SQLite_from_a_file_path_runs_the_store_suite_and_creates_the_folder()
    {
        var path = TempDb("app.db");
        var factory = SqlStoreFactory.Create("SqLite", path, All);

        Directory.Exists(Path.GetDirectoryName(path)).Should().BeTrue("SQLite does not create the database folder itself");
        await ExerciseAsync(factory);
    }

    [Fact]
    public async Task SQLite_keeps_WAL_on_first_use()
    {
        var path = TempDb("wal.db");
        var factory = SqlStoreFactory.Create("SqLite", path, All);
        await factory.CreateSchemaAsync(new[] { typeof(Gadget) });
        await factory.GetAsyncStore<Gadget>().CreateAsync(new Gadget { Name = "w", Weight = 1 });

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        ((string)command.ExecuteScalar()!).Should().Be("wal");
    }

    /// <summary>
    /// TASK-1006, the user's decision: an in-memory data source is refused when the factory is created. It was made
    /// into the path <c>&lt;working directory&gt;/:memory:</c>; on Windows the first operation then failed with "SQLite
    /// Error 14: unable to open database file" (measured), and on Linux that is a legal file name. An in-memory SQLite
    /// database lives only as long as one connection, and the connector opens one per operation.
    /// </summary>
    [Theory]
    [InlineData(":memory:")]
    [InlineData(" :MEMORY: ")]
    [InlineData("Data Source=:memory:")]
    [InlineData("Filename=:memory:")]
    [InlineData("Data Source=file::memory:")]
    [InlineData("Data Source=file::memory:?cache=shared")]
    [InlineData("file::memory:")]
    public void SQLite_refuses_an_in_memory_data_source(string connectionString)
    {
        var act = () => SqlStoreFactory.Create("SqLite", connectionString, All);

        act.Should().Throw<ArgumentException>().WithMessage("*in-memory*InMemory backend*file path*");
    }

    [Fact]
    public void SQLite_one_file_is_one_connector_however_the_path_is_written()
    {
        var path = TempDb("same.db");
        var absolute = SqlStoreFactory.Create("SqLite", path, All);
        var relative = SqlStoreFactory.Create("SqLite", Path.GetRelativePath(Environment.CurrentDirectory, path), All);
        var dataSource = SqlStoreFactory.Create("SqLite", $"Data Source={path}", All);

        relative.GetConnector().Should().BeSameAs(absolute.GetConnector());
        dataSource.GetConnector().Should().BeSameAs(absolute.GetConnector());
    }

    [Fact]
    public void SQLite_refuses_a_key_its_settings_cannot_carry()
    {
        var act = () => SqlStoreFactory.Create("SqLite", $"Data Source={TempDb("k.db")};Cache=Shared", All);

        act.Should().Throw<ArgumentException>().WithMessage("*SqLite*cannot use*'cache'*");
    }

    // ── connection strings of the server providers ───────────────────────────────────────────────

    [Fact]
    public void PostgreSQL_reads_every_key_its_settings_carry()
    {
        var f = (PostgreSQLSqlStoreFactory)SqlStoreFactory.Create("PostgreSQL",
            "Host=pg.example;Port=5433;Database=dracode;Username=app;Password='p;w=d';Timeout=20;Command Timeout=60;SSL Mode=Require", All);

        var s = f.PostgreSqlSettings;
        (s.Location, s.Port, s.Name, s.UserName, s.Password, s.UseSecure, s.ConnectionTimeout, s.CommandTimeout)
            .Should().Be(("pg.example", 5433, "dracode", "app", "p;w=d", true, 20, 60));
    }

    [Theory]
    [InlineData("Host=pg;Database=d;Pooling=false", "*cannot use*'pooling'*")]
    [InlineData("Host=pg;Database=d;Search Path=other", "*cannot use*'search path'*")]
    [InlineData("Host=pg;Database=d;SSL Mode=VerifyFull", "*SSL Mode 'VerifyFull' cannot be expressed*")]
    [InlineData("Database=d", "*needs Host*")]
    public void PostgreSQL_refuses_what_its_settings_cannot_carry(string connectionString, string message)
    {
        var act = () => SqlStoreFactory.Create("PostgreSQL", connectionString, All);

        act.Should().Throw<ArgumentException>().WithMessage(message);
    }

    [Fact]
    public void MySQL_reads_every_key_its_settings_carry()
    {
        var f = (MySQLSqlStoreFactory)SqlStoreFactory.Create("MySQL",
            "Server=my.example;Port=3307;Database=app;User ID=root;Password=x;Connection Timeout=9;SslMode=Required", All);

        var s = f.MySqlSettings;
        (s.Location, s.Port, s.Name, s.UserName, s.Password, s.UseSecure, s.ConnectionTimeout)
            .Should().Be(("my.example", 3307, "app", "root", "x", true, 9));
    }

    [Theory]
    [InlineData("Server=my;Database=d;Default Command Timeout=5", "*cannot use*'default command timeout'*")]
    [InlineData("Server=my;Database=d;SslMode=VerifyCA", "*SSL Mode 'VerifyCA' cannot be expressed*")]
    public void MySQL_refuses_what_its_settings_cannot_carry(string connectionString, string message)
    {
        var act = () => SqlStoreFactory.Create("MySQL", connectionString, All);

        act.Should().Throw<ArgumentException>().WithMessage(message);
    }

    [Fact]
    public void MSSql_reads_every_key_its_settings_carry()
    {
        var f = (MSSqlSqlStoreFactory)SqlStoreFactory.Create("MSSql",
            "Server=tcp:sql.example,1444;Database=app;User ID=sa;Password=x;Encrypt=False;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=7", All);

        var s = f.MSSqlSettings;
        (s.Location, s.Port, s.Name, s.UserName, s.Password, s.UseSecure, s.TrustServerCertificate, s.MultipleActiveResultSets, s.ConnectionTimeout)
            .Should().Be(("sql.example", 1444, "app", "sa", "x", false, true, true, 7));
    }

    [Theory]
    [InlineData("Server=sql;Database=d;Integrated Security=True", "*cannot use*'integrated security'*")]
    [InlineData("Server=sql\\SQLEXPRESS;Database=d", "*named instance*cannot be expressed*")]
    [InlineData("Server=sql;Database=d;Encrypt=Strict", "*Encrypt 'Strict' cannot be expressed*")]
    public void MSSql_refuses_what_its_settings_cannot_carry(string connectionString, string message)
    {
        var act = () => SqlStoreFactory.Create("MSSql", connectionString, All);

        act.Should().Throw<ArgumentException>().WithMessage(message);
    }

    // ── PostgreSQL, live ─────────────────────────────────────────────────────────────────────────

    private static string? PgHost => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");

    [Fact]
    public async Task PostgreSQL_live_runs_the_same_store_suite()
    {
        if (string.IsNullOrWhiteSpace(PgHost))
        {
            const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                                 + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }
        var port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
        var user = Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
        var password = Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
        var connectionString = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = PgHost, Port = port, Username = user, Password = password, Database = LiveDatabase.Name("PG"),
        }.ConnectionString;

        await ExerciseAsync(SqlStoreFactory.Create("PostgreSQL", connectionString, All));
    }
}
