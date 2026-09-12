using System;
using System.Data.Common;
using System.IO;
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
/// TASK-332, SQLite — the control. Needs no server, so it always runs, and it is the dialect that was
/// already working: its job is to show the fix did not regress the two providers that did.
/// </summary>
public class SqliteMigrationsTableDialectTests : MigrationsTableDialectTestsBase
{
    private readonly string _dbPath;

    public SqliteMigrationsTableDialectTests(ITestOutputHelper output) : base(output)
        => _dbPath = Path.Combine(Path.GetTempPath(), $"birko-mig332-{Guid.NewGuid():N}.db");

    protected override string Moniker => "Lt";
    protected override string DialectName => "SQLite";
    protected override string? HostVariable => null;
    protected override bool Available => true;

    // SQLite has no length-enforcing string type; ConvertType answers TEXT for every string, which
    // TASK-264 records as correct rather than as a gap.
    protected override bool DeclaresStringLength => false;

    private SqLiteSettings Settings()
        => new(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath));

    protected override AbstractConnector Connector() => DataBase.GetConnector<SqLiteConnector>(Settings());

    protected override DbConnection OpenRaw()
    {
        var connection = new SqliteConnection(Settings().GetConnectionString());
        connection.Open();
        return connection;
    }

    protected override void DropIfExists(DbConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS \"{table}\"";
        command.ExecuteNonQuery();
    }

    public override void Dispose()
    {
        base.Dispose();
        // Per TASK-276: release this database's pooled handles WITHOUT ClearAllPools(), which is
        // process-wide and disposes the sqlite3 handle a parallel sibling class is mid-statement on.
        try { SqliteConnection.ClearPool(new SqliteConnection(Settings().GetConnectionString())); } catch { }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    /// <summary>
    /// The one behaviour change this fix makes on a dialect that already worked, asserted rather than
    /// reasoned about.
    /// </summary>
    /// <remarks>
    /// Routing types through <c>ConvertType</c> means <c>Version</c> is declared <c>INTEGER</c> here instead
    /// of the previous hardcoded <c>BIGINT</c>, and in SQLite an <c>INTEGER PRIMARY KEY</c> column is an
    /// alias for the rowid. That is harmless for a version number but it is a real change, so it is measured:
    /// a full 64-bit-ranged version must still round-trip exactly. Existing databases are unaffected — the
    /// table is only ever created when absent.
    /// </remarks>
    [Fact]
    public void A_large_version_round_trips_through_sqlites_rowid_aliased_primary_key()
    {
        var migrations = MigrationsTable("Wide");
        Clean(migrations);

        var connector = Connector();
        var store = new Birko.Data.Migrations.SQL.SqlMigrationStore(
            () => connector.CreateConnection(connector.Settings),
            connector,
            new Birko.Data.Migrations.SQL.Settings.SqlMigrationSettings { MigrationsTable = migrations });

        store.Initialize();
        store.RecordMigration(new WideVersionMigration());

        store.GetAppliedVersions().Should().Contain(9007199254740993L,
            "a version wider than a double's exact integer range must survive the rowid alias unchanged");

        Clean(migrations);
    }

    private sealed class WideVersionMigration : AbstractMigration
    {
        public override long Version => 9007199254740993L;
        public override string Name => "WideVersion";
        public override void Up(Birko.Data.Migrations.Context.IMigrationContext context) { }
    }
}

/// <summary>
/// TASK-332, PostgreSQL — the other dialect that already worked, and the one that folds unquoted
/// identifiers. It is here to prove the change is behaviour-preserving where nothing was broken.
/// </summary>
public class PostgreSqlMigrationsTableDialectTests : MigrationsTableDialectTestsBase
{
    public PostgreSqlMigrationsTableDialectTests(ITestOutputHelper output) : base(output) { }

    protected override string Moniker => "Pg";
    protected override string DialectName => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    protected override AbstractConnector Connector() => DataBase.GetConnector<PostgreSQLConnector>(Settings());

    protected override DbConnection OpenRaw()
    {
        var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        return connection;
    }

    protected override void DropIfExists(DbConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS \"{table}\"";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A state table created by the <b>old</b> DDL is still readable and writable by the new code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the property that decided the shape of the fix, so it is measured rather than argued.
    /// Emitting the migrations table's columns <i>bare</i> — matching what
    /// <c>AbstractConnector.CreateTable</c> does for entity tables — was the tempting way to make one rule
    /// out of two. It is silently catastrophic here: PostgreSQL folds an unquoted identifier, so a bare
    /// <c>SELECT Version</c> asks for <c>version</c> while every already-deployed database stores
    /// <c>Version</c>, and every read of an existing database would raise <c>42703</c>. Keeping the columns
    /// quoted and changing only <i>which</i> delimiters are used makes the upgrade a no-op here.
    /// </para>
    /// <para>
    /// PostgreSQL is the dialect where this bites, because it is the only one of the four that both folds
    /// unquoted identifiers and could actually have an existing table — MySQL and SQL Server never managed to
    /// create one.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_state_table_left_by_the_previous_release_is_still_readable()
    {
        if (!RequireServer()) return;

        var migrations = MigrationsTable("Legacy");
        Clean(migrations);

        using (var connection = OpenRaw())
        {
            using var command = connection.CreateCommand();
            // Byte-for-byte the DDL this release replaced, ANSI quoting and all.
            command.CommandText = $@"
                CREATE TABLE ""{migrations}"" (
                    ""Version"" BIGINT PRIMARY KEY,
                    ""Name"" VARCHAR(255) NOT NULL,
                    ""Description"" TEXT,
                    ""CreatedAt"" TIMESTAMP NOT NULL,
                    ""AppliedAt"" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                );";
            command.ExecuteNonQuery();
        }

        var connector = Connector();
        var store = new Birko.Data.Migrations.SQL.SqlMigrationStore(
            () => connector.CreateConnection(connector.Settings),
            connector,
            new Birko.Data.Migrations.SQL.Settings.SqlMigrationSettings { MigrationsTable = migrations });

        // Initialize must find the existing table and leave it alone, and the DML must still resolve
        // against columns another release created.
        store.Initialize();
        store.RecordMigration(new LegacyProbeMigration());

        store.GetAppliedVersions().Should().Contain(42,
            "an upgraded deployment must keep reading the state table it already has");

        store.RemoveMigration(new LegacyProbeMigration());
        store.GetAppliedVersions().Should().BeEmpty();

        Clean(migrations);
    }

    private sealed class LegacyProbeMigration : AbstractMigration
    {
        public override long Version => 42;
        public override string Name => "LegacyProbe";
        public override void Up(Birko.Data.Migrations.Context.IMigrationContext context) { }
    }
}

/// <summary>
/// TASK-332, MySQL — where defect 1 lives. MySQL accepts <c>"</c> as an identifier delimiter only under
/// <c>ANSI_QUOTES</c>, which this framework never sets, so the ANSI-quoted state table could not be created
/// at all: <c>ERROR 1064</c>, before any model table.
/// </summary>
public class MySqlMigrationsTableDialectTests : MigrationsTableDialectTestsBase
{
    public MySqlMigrationsTableDialectTests(ITestOutputHelper output) : base(output) { }

    protected override string Moniker => "My";
    protected override string DialectName => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    // root/root, matching every existing MySQL live class in this tree -- TASK-266 records a run lost to
    // one class inventing its own default and producing 65 unrelated "Access denied" failures.
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";

    private static MySqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    protected override AbstractConnector Connector() => DataBase.GetConnector<MySQLConnector>(Settings());

    protected override DbConnection OpenRaw()
    {
        var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        return connection;
    }

    protected override void DropIfExists(DbConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{table}`";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// The concrete pin for defect 1: not one ANSI double quote reaches a MySQL statement.
    /// </summary>
    /// <remarks>
    /// Runs offline, because <c>ERROR 1064</c> is a plain syntax error and deserves a guard that does not
    /// need a server. Asserting the absence of the wrong delimiter rather than the presence of the right one
    /// is what makes this fail for the original defect and not merely for a reformatting.
    /// </remarks>
    [Fact]
    public void No_ansi_double_quote_reaches_a_mysql_statement()
    {
        if (!RequireServer()) return;

        Ddl(Connector(), "__MigDdlProbe").Should().NotContain("\"",
            "MySQL reads a double-quoted identifier as a string literal unless ANSI_QUOTES is in sql_mode, "
          + "which this framework never sets -- measured on 8.4.11 as ERROR 1064");
    }
}

/// <summary>
/// TASK-332, SQL Server — where defect 2 lives. <c>TIMESTAMP</c> is a deprecated synonym for
/// <c>ROWVERSION</c> here, and a table may carry at most one, so the two-timestamp DDL was rejected outright.
/// </summary>
public class MsSqlMigrationsTableDialectTests : MigrationsTableDialectTestsBase
{
    public MsSqlMigrationsTableDialectTests(ITestOutputHelper output) : base(output) { }

    protected override string Moniker => "Ms";
    protected override string DialectName => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port) { TrustServerCertificate = true };

    protected override AbstractConnector Connector() => DataBase.GetConnector<MSSqlConnector>(Settings());

    /// <summary>
    /// The SQL Server image ships no <c>MSSQL_DATABASE</c>, so unlike postgres/mysql the target database does
    /// not exist until something creates it. Idempotent, and connects to <c>master</c> because
    /// <c>CREATE DATABASE</c> cannot run against the database it creates.
    /// </summary>
    protected override void Bootstrap()
    {
        var master = new MSSqlSettings(Host!, "master", User, Password, Port) { TrustServerCertificate = true };
        using var connection = new SqlConnection(master.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "IF DB_ID(@d) IS NULL "
          + "BEGIN "
          + "  DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@d); "
          + "  EXEC sp_executesql @sql; "
          + "END";
        command.Parameters.AddWithValue("@d", Database);
        command.ExecuteNonQuery();
    }

    protected override DbConnection OpenRaw()
    {
        var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        return connection;
    }

    protected override void DropIfExists(DbConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS [{table}]";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// The concrete pin for defect 2: no <c>TIMESTAMP</c> column type reaches a T-SQL statement.
    /// </summary>
    [Fact]
    public void No_timestamp_column_type_reaches_a_tsql_statement()
    {
        if (!RequireServer()) return;

        // The DEFAULT clause legitimately names CURRENT_TIMESTAMP -- a niladic function, not a type, and
        // valid T-SQL (measured on 2022 CU26). Removing it first is what makes this assertion about the
        // column TYPE rather than about the token; the first version of this test failed on its own
        // default clause.
        var types = Ddl(Connector(), "__MigDdlProbe").Replace("DEFAULT CURRENT_TIMESTAMP", string.Empty);

        types.Should().NotContain("TIMESTAMP",
            "TIMESTAMP is a deprecated synonym for ROWVERSION in T-SQL and a table may have at most one, so "
          + "declaring CreatedAt and AppliedAt that way was Msg 2738 on SQL Server 2022 CU26");
    }

    /// <summary>
    /// And the catalogue agrees — the companion to the pin above, because a string assertion cannot tell a
    /// datetime column from a row-version one.
    /// </summary>
    [Fact]
    public void The_timestamp_columns_are_real_datetimes_on_sql_server()
    {
        if (!RequireServer()) return;

        var migrations = MigrationsTable("Cat");
        Clean(migrations);

        var connector = Connector();
        var store = new Birko.Data.Migrations.SQL.SqlMigrationStore(
            () => connector.CreateConnection(connector.Settings),
            connector,
            new Birko.Data.Migrations.SQL.Settings.SqlMigrationSettings { MigrationsTable = migrations });
        store.Initialize();

        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TYPE_NAME(system_type_id) FROM sys.columns "
          + "WHERE object_id = OBJECT_ID(@t) AND name = 'AppliedAt'";
        command.Parameters.Add(new SqlParameter("@t", migrations));
        var type = command.ExecuteScalar() as string;

        type.Should().NotBeNull("the state table must exist");
        type.Should().NotBe("timestamp", "a ROWVERSION column cannot hold the time a migration was applied");

        Clean(migrations);
    }
}
