using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.Models;
using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

[Table("T637Rows")]
public class T637Row : AbstractModel
{
    public string? Name { get; set; }
    public int Qty { get; set; }

    [NamedField("amount_col")]
    public int Amount { get; set; }
}

/// <summary>
/// TASK-637, live: a migration's data migrator and <c>RenameField</c> take a model's property names. They quoted them, so on
/// PostgreSQL <c>"Name"</c> missed the folded column <c>name</c> (42703). Columns are now emitted bare (rule 17), resolved
/// against the mapped table's metadata where there is one (rule 15: <c>Amount</c> is the column <c>amount_col</c>), and a
/// name that is not a plain identifier is refused (rule 33).
/// </summary>
public abstract class Task637MigratorColumnLiveTestsBase<DB> : IDisposable where DB : AbstractConnector
{
    protected readonly ITestOutputHelper Output;
    protected Task637MigratorColumnLiveTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract Birko.Configuration.Settings Settings();
    protected abstract DbConnection OpenRaw();

    protected bool RequireServer()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private async Task<AsyncDataBaseBulkStore<DB, T637Row>> Fresh()
    {
        var store = new AsyncDataBaseBulkStore<DB, T637Row>();
        store.SetSettings(Settings());
        store.Connector!.DropTable(new[] { typeof(T637Row) });
        await store.InitAsync();
        return store;
    }

    public void Dispose()
    {
        if (HostVariable != null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return;
        var store = new AsyncDataBaseBulkStore<DB, T637Row>();
        store.SetSettings(Settings());
        try { store.Connector!.DropTable(new[] { typeof(T637Row) }); } catch { }
    }

    [Fact]
    public async Task The_migrator_takes_the_models_property_names()
    {
        if (!RequireServer()) return;
        var store = await Fresh();
        using var connection = OpenRaw();
        var data = new SqlDataMigrator(connection, null, store.Connector!);

        data.BulkInsert("T637Rows", new[]
        {
            new Dictionary<string, object> { ["Guid"] = Guid.NewGuid(), ["Name"] = "a", ["Qty"] = 1, ["Amount"] = 10 },
            new Dictionary<string, object> { ["Guid"] = Guid.NewGuid(), ["Name"] = "b", ["Qty"] = 2, ["Amount"] = 20 },
        });
        data.UpdateDocuments("T637Rows", "{\"Name\":\"a\"}", new Dictionary<string, object> { ["Qty"] = 5, ["Amount"] = 50 });

        data.CountDocuments("T637Rows", "{\"Qty\":5}").Should().Be(1);
        var a = (await store.ReadAsync(x => x.Name == "a", null, null, null)).Single();
        a.Qty.Should().Be(5);
        a.Amount.Should().Be(50, "Amount is the column amount_col, resolved from the model's metadata");

        data.DeleteDocuments("T637Rows", "{\"Name\":\"b\"}");
        (await store.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RenameField_renames_a_model_created_column()
    {
        if (!RequireServer()) return;
        var store = await Fresh();
        await store.CreateAsync(new T637Row { Name = "r", Qty = 7 });
        using var connection = OpenRaw();
        var schema = new SqlSchemaBuilder(connection, null, store.Connector!);

        schema.RenameField("T637Rows", "Qty", "Quantity");
        schema.RenameField("T637Rows", "Quantity", "Qty");

        (await store.ReadAsync(null, null, null, null)).Single().Qty.Should().Be(7, "the column is back under the model's name");
    }

    [Fact]
    public async Task A_column_name_that_is_not_an_identifier_is_refused()
    {
        if (!RequireServer()) return;
        var store = await Fresh();
        using var connection = OpenRaw();
        var data = new SqlDataMigrator(connection, null, store.Connector!);

        data.Invoking(d => d.BulkInsert("T637Rows", new[] { new Dictionary<string, object> { ["Name) VALUES ('x'); --"] = "x" } }))
            .Should().Throw<ArgumentException>();
        new SqlSchemaBuilder(connection, null, store.Connector!)
            .Invoking(s => s.RenameField("T637Rows", "Qty", "Qty; DROP TABLE x"))
            .Should().Throw<ArgumentException>();
    }
}

public sealed class SqliteTask637Tests : Task637MigratorColumnLiveTestsBase<SqLiteConnector>
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t637-{Guid.NewGuid():N}.db");
    public SqliteTask637Tests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override Birko.Configuration.Settings Settings() => new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
    protected override DbConnection OpenRaw() { var c = new SqliteConnection(((SqLiteSettings)Settings()).GetConnectionString()); c.Open(); return c; }
}

public sealed class PostgreSqlTask637Tests : Task637MigratorColumnLiveTestsBase<PostgreSQLConnector>
{
    public PostgreSqlTask637Tests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    private static PostgreSqlSettings Pg() => new(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override Birko.Configuration.Settings Settings() => Pg();
    protected override DbConnection OpenRaw() { var c = new NpgsqlConnection(Pg().GetConnectionString()); c.Open(); return c; }
}

public sealed class MySqlTask637Tests : Task637MigratorColumnLiveTestsBase<MySQLConnector>
{
    public MySqlTask637Tests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    private static MySqlSettings My() => new(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override Birko.Configuration.Settings Settings() => My();
    protected override DbConnection OpenRaw() { var c = new MySqlConnection(My().GetConnectionString()); c.Open(); return c; }
}

public sealed class MSSqlTask637Tests : Task637MigratorColumnLiveTestsBase<MSSqlConnector>
{
    public MSSqlTask637Tests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    private static MSSqlSettings Ms() => new(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override Birko.Configuration.Settings Settings() => Ms();
    protected override DbConnection OpenRaw() { var c = new SqlConnection(Ms().GetConnectionString()); c.Open(); return c; }
}
