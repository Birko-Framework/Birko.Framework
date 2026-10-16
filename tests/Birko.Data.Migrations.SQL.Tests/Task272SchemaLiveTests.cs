using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

[Table("T272Evts", Schema = "t272rep")]
public class T272Evt : AbstractModel
{
    [MaxLengthField(50)]
    [IndexedField("IX_T272Evts_Name")]
    public string? Name { get; set; }
    public int Qty { get; set; }
}

/// <summary>No schema of its own: it takes the settings' default schema, when there is one.</summary>
[Table("T272Plain")]
public class T272Plain : AbstractModel
{
    public string? Name { get; set; }
}

/// <summary>A view joining the schema'd table to a plain one, on the name.</summary>
[View(typeof(T272Evt), typeof(T272Plain), nameof(T272Evt.Name), nameof(T272Plain.Name))]
public class T272EvtPlainView
{
    [ViewField(typeof(T272Evt), nameof(T272Evt.Qty))]
    public int Qty { get; set; }

    [ViewField(typeof(T272Plain), nameof(T272Plain.Name))]
    public string? Name { get; set; }
}

/// <summary>
/// TASK-272, live: an entity declaring a schema is created, written, read, indexed and dropped in it, asserted against the
/// catalogue: <c>t272rep.T272Evts</c> on PostgreSQL and SQL Server, the table <c>t272rep__T272Evts</c> on MySQL and SQLite.
/// </summary>
public abstract class Task272SchemaLiveTestsBase<DB> : IDisposable where DB : AbstractConnector
{
    protected readonly ITestOutputHelper Output;
    protected Task272SchemaLiveTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract Birko.Configuration.Settings Settings(string? schema = null);
    protected abstract DbConnection OpenRaw();
    protected abstract bool HasSchemas { get; }

    /// <summary>
    /// Whether this project's connector can create a persistent view: SQLite's and SQL Server's view DDL lives in their
    /// .View packages, which this project does not import (their own suites cover it).
    /// </summary>
    protected virtual bool PersistentViewsHere => true;

    protected bool RequireServer()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private AsyncDataBaseBulkStore<DB, T> Store<T>(string? schema = null) where T : AbstractModel
    {
        var store = new AsyncDataBaseBulkStore<DB, T>();
        store.SetSettings(Settings(schema));
        return store;
    }

    /// <summary>The relation's catalogue identity: (schema or null, stored name).</summary>
    private (string? Schema, string Name) Expected(string schema, string table)
        => HasSchemas ? (schema, table) : (null, schema + "__" + table);

    protected abstract string TableCountSql(string? schema, string table);
    protected abstract Birko.Data.SQL.IndexManagement.SqlIndexManager Manager(AbstractConnector connector);
    protected abstract string IndexCountSql(string? schema, string table, string index);

    private long Scalar(string sql)
    {
        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        Output.WriteLine(sql);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose()
    {
        if (HostVariable != null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return;
        try { Store<T272Evt>().Connector!.DropTable(new[] { typeof(T272Evt) }); } catch { }
        try { Store<T272Plain>("t272def").Connector!.DropTable(new[] { typeof(T272Plain) }); } catch { }
    }

    [Fact]
    public void The_capability_says_which_providers_have_schemas()
    {
        if (!RequireServer()) return;
        Store<T272Evt>().Connector!.SupportsSchemas.Should().Be(HasSchemas);
    }

    [Fact]
    public async Task An_entity_with_a_schema_lives_and_is_written_there()
    {
        if (!RequireServer()) return;
        var store = Store<T272Evt>();
        await store.InitAsync();
        var (schema, name) = Expected("t272rep", "T272Evts");

        Scalar(TableCountSql(schema, name)).Should().Be(1, "the table is created in its schema");
        Scalar(TableCountSql(null, "T272Evts")).Should().Be(0, "and not in the default one");
        Scalar(IndexCountSql(schema, name, "IX_T272Evts_Name")).Should().Be(1, "its index goes on that table");

        var a = new T272Evt { Name = "a", Qty = 1 };
        await store.CreateAsync(a);
        await store.CreateAsync(new[] { new T272Evt { Name = "b", Qty = 2 }, new T272Evt { Name = "c", Qty = 3 } });
        a.Qty = 10;
        await store.UpdateAsync(a);
        await store.UpdateAsync(x => x.Name == "b", new Birko.Data.Stores.PropertyUpdate<T272Evt>().Set(x => x.Qty, 20));

        var rows = (await store.ReadAsync(null, null, null, null)).OrderBy(r => r.Name).ToList();
        rows.Select(r => (r.Name, r.Qty)).Should().Equal(("a", 10), ("b", 20), ("c", 3));
        (await store.CountAsync(x => x.Qty > 5)).Should().Be(2);
        Scalar($"SELECT COUNT(*) FROM {store.Connector!.TableIdentifier("T272Evts")}").Should().Be(3, "the rows are in the schema's table");

        await store.DeleteAsync(rows.Where(r => r.Name != "a"));
        await store.DeleteAsync(x => x.Name == "a");
        (await store.CountAsync()).Should().Be(0);

        store.Connector!.DropTable(new[] { typeof(T272Evt) });
        Scalar(TableCountSql(schema, name)).Should().Be(0, "dropped from its schema");
    }

    /// <summary>Phase 2: every catalogue question about the entity is asked of its schema, not the default one.</summary>
    [Fact]
    public async Task Drift_index_lookups_and_existence_find_the_table_in_its_schema()
    {
        if (!RequireServer()) return;
        var store = Store<T272Evt>();
        await store.InitAsync();
        var connector = store.Connector!;

        var report = connector.DetectDrift(typeof(T272Evt));
        Output.WriteLine(report.ToString());
        report.TableExists.Should().BeTrue("the stored columns are read from the table's own schema");
        report.IsClean.Should().BeTrue();

        var manager = Manager(connector);
        (await manager.ExistsAsync("IX_T272Evts_Name", "T272Evts")).Should().BeTrue();
        (await manager.ListAsync("T272Evts")).Select(i => i.Name).Should().Contain("IX_T272Evts_Name");

        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = connector.TableExistsSql!;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tableName";
        parameter.Value = connector.TableExistsArgument("T272Evts");
        command.Parameters.Add(parameter);
        Convert.ToInt64(command.ExecuteScalar()).Should().Be(1, "the existence query resolves the schema too");
    }

    /// <summary>
    /// Phase 2: a schema'd table that vanishes is still recognised as one this connector created, so the store heals on
    /// the next write. On MySQL and SQLite the provider names the STORED table (<c>t272rep__T272Evts</c>), which is
    /// recorded under its logical name.
    /// </summary>
    [Fact]
    public async Task A_vanished_schema_table_heals_on_the_next_write()
    {
        if (!RequireServer()) return;
        var store = Store<T272Evt>();
        await store.CreateAsync(new T272Evt { Name = "seed" });

        using (var connection = OpenRaw())
        using (var drop = connection.CreateCommand())
        {
            drop.CommandText = "DROP TABLE " + store.Connector!.TableIdentifier("T272Evts");
            drop.ExecuteNonQuery();
        }

        async Task<bool> Attempt(string name)
        {
            try { await store.CreateAsync(new T272Evt { Name = name }); return true; }
            catch (Exception ex) { Output.WriteLine(ex.Message); return false; }
        }

        (await Attempt("w1")).Should().BeFalse("the write against the missing table is reported");
        (await Attempt("w2")).Should().BeTrue("the anomaly was recognised, so the store re-ensured its table");
        (await store.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// Phase 3: a view over a schema'd table, both the persistent view's DDL and the on-the-fly SELECT. On MySQL and SQLite
    /// the stored table is <c>t272rep__T272Evts</c> while the view's column qualifiers name <c>T272Evts</c>, so the FROM
    /// must alias it.
    /// </summary>
    [Fact]
    public async Task A_view_over_a_schema_table_reads_it()
    {
        if (!RequireServer()) return;
        var evts = Store<T272Evt>();
        var plain = Store<T272Plain>();
        evts.Connector!.DropTable(new[] { typeof(T272Evt) });
        plain.Connector!.DropTable(new[] { typeof(T272Plain) });
        await evts.CreateAsync(new[] { new T272Evt { Name = "a", Qty = 1 }, new T272Evt { Name = "b", Qty = 2 } });
        await plain.CreateAsync(new T272Plain { Name = "a" });
        var connector = evts.Connector!;
        var view = DataBase.LoadView(typeof(T272EvtPlainView));

        connector.SelectCount(view).Should().Be(1, "the on-the-fly view joins the schema'd table");
        if (!PersistentViewsHere)
        {
            plain.Connector!.DropTable(new[] { typeof(T272Plain) });
            return;
        }

        try { connector.DropView("t272_view"); } catch { }
        connector.CreateView(typeof(T272EvtPlainView), "t272_view");
        try
        {
            Scalar($"SELECT COUNT(*) FROM {connector.QuoteIdentifier("t272_view")}").Should().Be(1, "the persistent view reads it too");
        }
        finally
        {
            connector.DropView("t272_view");
            plain.Connector!.DropTable(new[] { typeof(T272Plain) });
        }
    }

    /// <summary>Phase 4: a migration's schema builder and data migrator address the entity's table in its schema.</summary>
    [Fact]
    public async Task Migrations_address_the_table_in_its_schema()
    {
        if (!RequireServer()) return;
        var store = Store<T272Evt>();
        store.Connector!.DropTable(new[] { typeof(T272Evt) });
        await store.InitAsync();
        await store.CreateAsync(new T272Evt { Name = "m", Qty = 5 });
        var connector = store.Connector!;

        using var connection = OpenRaw();
        var schema = new Birko.Data.Migrations.SQL.Context.SqlSchemaBuilder(connection, null, connector);
        var data = new Birko.Data.Migrations.SQL.Context.SqlDataMigrator(connection, null, connector);

        schema.CollectionExists("T272Evts").Should().BeTrue();
        data.CountDocuments("T272Evts").Should().Be(1);
        // The model's property names (TASK-637 made the migrator take them on PostgreSQL too).
        data.BulkInsert("T272Evts", new[]
        {
            new System.Collections.Generic.Dictionary<string, object> { ["Guid"] = Guid.NewGuid(), ["Name"] = "n", ["Qty"] = 6 },
        });
        data.CountDocuments("T272Evts").Should().Be(2, "the insert landed in the schema's table");

        schema.RenameField("T272Evts", "Qty", "Quantity");
        schema.RenameField("T272Evts", "Quantity", "Qty");
        data.DeleteDocuments("T272Evts", "{\"Name\":\"n\"}");
        (await store.CountAsync()).Should().Be(1, "the store and the migrator see one table");
    }

    [Fact]
    public async Task The_settings_default_schema_holds_a_table_that_declares_none()
    {
        if (!RequireServer()) return;
        var store = Store<T272Plain>("t272def");
        await store.InitAsync();
        await store.CreateAsync(new T272Plain { Name = "x" });

        var (schema, name) = Expected("t272def", "T272Plain");
        Scalar(TableCountSql(schema, name)).Should().Be(1);
        (await store.ReadAsync(null, null, null, null)).Should().ContainSingle().Which.Name.Should().Be("x");
    }
}

public class Task272SchemaRefusalTests
{
    [Table("t272clash__T272Thing")]
    public class Flat : AbstractModel { public string? Name { get; set; } }

    [Table("T272Thing", Schema = "t272clash")]
    public class Scoped : AbstractModel { public string? Name { get; set; } }

    [Table("T272Bad", Schema = "bad schema")]
    public class Bad : AbstractModel { public string? Name { get; set; } }

    [Fact]
    public void Two_tables_that_would_be_one_table_on_MySQL_and_SQLite_are_refused()
    {
        DataBase.LoadTable(typeof(Flat));
        FluentActions.Invoking(() => DataBase.LoadTable(typeof(Scoped)))
            .Should().Throw<Birko.Data.Exceptions.TableAttributeException>().WithMessage("*t272clash__T272Thing*");
    }

    [Fact]
    public void A_schema_that_is_not_a_plain_identifier_is_refused()
    {
        FluentActions.Invoking(() => DataBase.LoadTable(typeof(Bad)))
            .Should().Throw<Birko.Data.Exceptions.TableAttributeException>().WithMessage("*plain identifier*");
    }

    [Fact]
    public void A_settings_schema_is_part_of_the_settings_identity()
    {
        var plain = new PostgreSqlSettings("h", "db", "u", "p");
        var scoped = new PostgreSqlSettings("h", "db", "u", "p") { Schema = "reporting" };
        scoped.GetId().Should().NotBe(plain.GetId(), "connectors are cached by it");
        plain.GetId().Should().Be(new PostgreSqlSettings("h", "db", "u", "p").GetId());
    }
}

public sealed class SqliteTask272SchemaLiveTests : Task272SchemaLiveTestsBase<SqLiteConnector>
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t272-{Guid.NewGuid():N}.db");
    public SqliteTask272SchemaLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override bool HasSchemas => false;
    protected override bool PersistentViewsHere => false;
    protected override Birko.Configuration.Settings Settings(string? schema = null)
        => new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)) { Schema = schema };
    protected override DbConnection OpenRaw() { var c = new SqliteConnection(((SqLiteSettings)Settings()).GetConnectionString()); c.Open(); return c; }
    protected override string TableCountSql(string? schema, string table) => $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
    protected override string IndexCountSql(string? schema, string table, string index) => $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND tbl_name='{table}' AND name='{index}'";
    protected override Birko.Data.SQL.IndexManagement.SqlIndexManager Manager(AbstractConnector c) => new Birko.Data.SQL.SQLite.IndexManagement.SqLiteIndexManager(c);
}

public sealed class PostgreSqlTask272SchemaLiveTests : Task272SchemaLiveTestsBase<PostgreSQLConnector>
{
    public PostgreSqlTask272SchemaLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override bool HasSchemas => true;
    private static PostgreSqlSettings Pg(string? schema) => new(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432, Schema = schema };
    protected override Birko.Configuration.Settings Settings(string? schema = null) => Pg(schema);
    protected override DbConnection OpenRaw() { var c = new NpgsqlConnection(Pg(null).GetConnectionString()); c.Open(); return c; }
    protected override string TableCountSql(string? schema, string table)
        => $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = {(schema == null ? "current_schema()" : $"'{schema}'")} AND table_name = '{table}'";
    protected override string IndexCountSql(string? schema, string table, string index)
        => $"SELECT COUNT(*) FROM pg_indexes WHERE schemaname = '{schema}' AND tablename = '{table}' AND indexname = '{index}'";
    protected override Birko.Data.SQL.IndexManagement.SqlIndexManager Manager(AbstractConnector c) => new Birko.Data.SQL.PostgreSQL.IndexManagement.PostgreSqlIndexManager(c);
}

public sealed class MySqlTask272SchemaLiveTests : Task272SchemaLiveTestsBase<MySQLConnector>
{
    public MySqlTask272SchemaLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override bool HasSchemas => false;
    private static MySqlSettings My(string? schema) => new(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306, Schema = schema };
    protected override Birko.Configuration.Settings Settings(string? schema = null) => My(schema);
    protected override DbConnection OpenRaw() { var c = new MySqlConnection(My(null).GetConnectionString()); c.Open(); return c; }
    protected override string TableCountSql(string? schema, string table)
        => $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{table}'";
    protected override string IndexCountSql(string? schema, string table, string index)
        => $"SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = '{table}' AND index_name = '{index}'";
    protected override Birko.Data.SQL.IndexManagement.SqlIndexManager Manager(AbstractConnector c) => new Birko.Data.SQL.MySQL.IndexManagement.MySqlIndexManager(c);
}

public sealed class MSSqlTask272SchemaLiveTests : Task272SchemaLiveTestsBase<MSSqlConnector>
{
    public MSSqlTask272SchemaLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override bool HasSchemas => true;
    protected override bool PersistentViewsHere => false;
    private static MSSqlSettings Ms(string? schema) => new(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true, Schema = schema };
    protected override Birko.Configuration.Settings Settings(string? schema = null) => Ms(schema);
    protected override DbConnection OpenRaw() { var c = new SqlConnection(Ms(null).GetConnectionString()); c.Open(); return c; }
    protected override string TableCountSql(string? schema, string table)
        => $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = {(schema == null ? "SCHEMA_NAME()" : $"'{schema}'")} AND TABLE_NAME = '{table}'";
    protected override string IndexCountSql(string? schema, string table, string index)
        => $"SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id "
         + $"WHERE s.name = '{schema}' AND t.name = '{table}' AND i.name = '{index}'";
    protected override Birko.Data.SQL.IndexManagement.SqlIndexManager Manager(AbstractConnector c) => new Birko.Data.SQL.MSSql.IndexManagement.MSSqlIndexManager(c);
}
