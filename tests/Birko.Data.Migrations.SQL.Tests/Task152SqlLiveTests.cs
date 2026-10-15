using System;
using System.Data.Common;
using System.IO;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// TASK-152, live on the four SQL providers: <c>CollectionExists</c> answers for the schema or database an unqualified
/// <c>CREATE TABLE</c> would use, not for a same-named table elsewhere (SH-M181); and a JSON filter's null and not-equal
/// mean what the filter dialect means (SH-M176): <c>{"x": null}</c> matches NULL, <c>$ne</c> also matches NULL.
/// </summary>
public abstract class Task152SqlLiveTestsBase : IDisposable
{
    protected readonly ITestOutputHelper Output;
    protected readonly string Table = "T152_" + Guid.NewGuid().ToString("N").Substring(0, 10);

    protected Task152SqlLiveTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();
    protected abstract DbConnection OpenRaw();
    /// <summary>Creates a table named <see cref="Table"/> somewhere a bare name does NOT resolve to; null when the provider has no such place.</summary>
    protected abstract string? CreateElsewhereSql { get; }
    protected abstract string? DropElsewhereSql { get; }
    protected abstract string Quote(string identifier);

    protected bool RequireServer()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    protected void Exec(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public virtual void Dispose()
    {
        if (!RequireServerQuietly()) return;
        try
        {
            using var connection = OpenRaw();
            try { Exec(connection, $"DROP TABLE {Quote(Table)}"); } catch { }
            if (DropElsewhereSql != null) { try { Exec(connection, DropElsewhereSql); } catch { } }
        }
        catch { }
    }

    private bool RequireServerQuietly() => HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable));

    [Fact]
    public void A_table_of_that_name_in_another_schema_or_database_does_not_count()
    {
        if (!RequireServer()) return;
        if (CreateElsewhereSql == null) return;
        var connector = Connector();
        using var connection = OpenRaw();
        Exec(connection, CreateElsewhereSql);
        var schema = new SqlSchemaBuilder(connection, null, connector);

        schema.CollectionExists(Table).Should().BeFalse("the only table of that name is somewhere a bare name does not resolve to");

        Exec(connection, $"CREATE TABLE {Quote(Table)} ({Quote("Id")} INT)");
        schema.CollectionExists(Table).Should().BeTrue();
    }

    [Fact]
    public void Null_and_not_equal_mean_what_the_filter_means()
    {
        if (!RequireServer()) return;
        var connector = Connector();
        using var connection = OpenRaw();
        Exec(connection, $"CREATE TABLE {Quote(Table)} ({Quote("Id")} INT, {Quote("Kind")} VARCHAR(10) NULL)");
        Exec(connection, $"INSERT INTO {Quote(Table)} ({Quote("Id")}, {Quote("Kind")}) VALUES (1, 'a'), (2, 'b'), (3, NULL)");
        var data = new SqlDataMigrator(connection, null, connector);

        data.CountDocuments(Table, "{\"Kind\":null}").Should().Be(1);
        data.CountDocuments(Table, "{\"Kind\":{\"$ne\":null}}").Should().Be(2);
        data.CountDocuments(Table, "{\"Kind\":{\"$ne\":\"a\"}}").Should().Be(2, "b, and the row whose Kind is NULL");
        data.CountDocuments(Table, "{\"Kind\":{\"$eq\":\"a\"}}").Should().Be(1);
        data.Invoking(d => d.CountDocuments(Table, "{\"Kind\":{\"$in\":[\"a\",\"b\"]}}")).Should().Throw<NotSupportedException>();

        data.DeleteDocuments(Table, "{\"Kind\":{\"$ne\":\"a\"}}");
        data.CountDocuments(Table).Should().Be(1);
    }
}

public sealed class SqliteTask152LiveTests : Task152SqlLiveTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t152-{Guid.NewGuid():N}.db");
    public SqliteTask152LiveTests(ITestOutputHelper output) : base(output) { }
    private SqLiteSettings Settings() => new(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => DataBase.GetConnector<SqLiteConnector>(Settings());
    protected override DbConnection OpenRaw() { var c = new SqliteConnection(Settings().GetConnectionString()); c.Open(); return c; }
    protected override string? CreateElsewhereSql => null;
    protected override string? DropElsewhereSql => null;
    protected override string Quote(string identifier) => "\"" + identifier + "\"";

    public override void Dispose()
    {
        base.Dispose();
        try { SqliteConnection.ClearPool(new SqliteConnection(Settings().GetConnectionString())); } catch { }
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }
}

public sealed class PostgreSqlTask152LiveTests : Task152SqlLiveTestsBase
{
    public PostgreSqlTask152LiveTests(ITestOutputHelper output) : base(output) { }
    private static PostgreSqlSettings Settings() => new(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => DataBase.GetConnector<PostgreSQLConnector>(Settings());
    protected override DbConnection OpenRaw() { var c = new NpgsqlConnection(Settings().GetConnectionString()); c.Open(); return c; }
    protected override string? CreateElsewhereSql => $"CREATE SCHEMA IF NOT EXISTS t152other; CREATE TABLE t152other.\"{Table}\" (\"Id\" INT)";
    protected override string? DropElsewhereSql => $"DROP TABLE IF EXISTS t152other.\"{Table}\"";
    protected override string Quote(string identifier) => "\"" + identifier + "\"";
}

public sealed class MySqlTask152LiveTests : Task152SqlLiveTestsBase
{
    public MySqlTask152LiveTests(ITestOutputHelper output) : base(output) { }
    private static MySqlSettings Settings() => new(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => DataBase.GetConnector<MySQLConnector>(Settings());
    protected override DbConnection OpenRaw() { var c = new MySqlConnection(Settings().GetConnectionString()); c.Open(); return c; }
    protected override string? CreateElsewhereSql => $"CREATE DATABASE IF NOT EXISTS t152other; CREATE TABLE t152other.`{Table}` (`Id` INT)";
    protected override string? DropElsewhereSql => $"DROP TABLE IF EXISTS t152other.`{Table}`";
    protected override string Quote(string identifier) => "`" + identifier + "`";
}

public sealed class MSSqlTask152LiveTests : Task152SqlLiveTestsBase
{
    public MSSqlTask152LiveTests(ITestOutputHelper output) : base(output) { }
    private static MSSqlSettings Settings() => new(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => DataBase.GetConnector<MSSqlConnector>(Settings());
    protected override DbConnection OpenRaw() { var c = new SqlConnection(Settings().GetConnectionString()); c.Open(); return c; }
    protected override string? CreateElsewhereSql
        => $"IF SCHEMA_ID('t152other') IS NULL EXEC('CREATE SCHEMA t152other'); CREATE TABLE t152other.[{Table}] ([Id] INT)";
    protected override string? DropElsewhereSql => $"DROP TABLE IF EXISTS t152other.[{Table}]";
    protected override string Quote(string identifier) => "[" + identifier + "]";
}
