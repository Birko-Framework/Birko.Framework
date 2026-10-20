using Birko.Configuration;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.IndexManagement;
using Birko.Data.SQL.MySQL.IndexManagement;
using Birko.Data.SQL.PostgreSQL.IndexManagement;
using Birko.Data.SQL.SQLite.IndexManagement;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Xunit.Abstractions;

namespace Birko.Data.Localization.SQL.Tests;

/// <summary>SQLite runs everywhere; the server providers run when their <c>BIRKO_*_HOST</c> is set (live-tests.yml).</summary>
public sealed class SqLiteTranslationTableTests : TranslationTableTests<SqLiteConnector>
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-localization-sql-{Guid.NewGuid():N}");

    public SqLiteTranslationTableTests(ITestOutputHelper output) : base(output)
    {
        Directory.CreateDirectory(_root);
    }

    protected override string Label => "SQLite";
    protected override string? HostVariable => null;
    protected override PasswordSettings Settings() => new SqLiteSettings(_root, "localization.db");
    protected override IIndexManager Indexes(AbstractConnectorBase connector) => new SqLiteIndexManager(connector);

    public override void Dispose()
    {
        // Only this database's pool: a process-wide ClearAllPools breaks parallel siblings (rule 61).
        using (var probe = new Microsoft.Data.Sqlite.SqliteConnection(((SqLiteSettings)Settings()).GetConnectionString()))
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(probe);
        }
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class PostgreSqlTranslationTableTests : TranslationTableTests<PostgreSQLConnector>
{
    public PostgreSqlTranslationTableTests(ITestOutputHelper output) : base(output)
    {
    }

    protected override string Label => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IIndexManager Indexes(AbstractConnectorBase connector) => new PostgreSqlIndexManager(connector);
    protected override string? CurrentSchemaSql => "current_schema()";
    protected override bool? RetriesInsideATransaction => false;
}

public sealed class MySqlTranslationTableTests : TranslationTableTests<MySQLConnector>
{
    public MySqlTranslationTableTests(ITestOutputHelper output) : base(output)
    {
    }

    protected override string Label => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IIndexManager Indexes(AbstractConnectorBase connector) => new MySqlIndexManager(connector);
    protected override string? CurrentSchemaSql => "DATABASE()";
    // Measured (TASK-684): the refused INSERT leaves the transaction usable, but the re-read runs on the snapshot taken at
    // the transaction's first read (InnoDB REPEATABLE READ), before the other writer committed, so it finds no row and
    // the refusal stands.
    protected override bool? RetriesInsideATransaction => false;
    protected override string? OldShapeTableDdl(string table) =>
        $"CREATE TABLE `{table}` (`EntityGuid` char(36) NOT NULL, `EntityType` varchar(255) NOT NULL, "
        + "`FieldName` varchar(255) NOT NULL, `Culture` varchar(255) NOT NULL, `Value` longtext NOT NULL, "
        + "`UpdatedAt` datetime(6) DEFAULT NULL, `ValueHash` varchar(255) DEFAULT NULL, `Guid` char(36) NOT NULL, "
        + "PRIMARY KEY (`Guid`)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";
}

public sealed class MSSqlTranslationTableTests : TranslationTableTests<MSSqlConnector>
{
    public MSSqlTranslationTableTests(ITestOutputHelper output) : base(output)
    {
    }

    protected override string Label => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IIndexManager Indexes(AbstractConnectorBase connector) => new MSSqlIndexManager(connector);
    protected override string? CurrentSchemaSql => "SCHEMA_NAME()";
    protected override bool? RetriesInsideATransaction => true;
}
