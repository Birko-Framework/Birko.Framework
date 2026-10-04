using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SchemaDrift;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-513 — a <c>decimal</c> on SQLite is stored exactly, and still orders, compares and adds as a number.
/// <para>
/// Before: <c>REAL</c> (unprecisioned) or <c>NUMERIC(p,s)</c> (declared), both an 8-byte float on SQLite.
/// Measured then: <c>1234567890123456.123456m</c> read back <c>1234567890123456</c>, <c>decimal.MaxValue</c>
/// wrote and threw <c>OverflowException</c> on read, and <c>10.10 + 0.20</c> stored <c>10.299999999999999</c>.
/// Now: <c>TEXT COLLATE BIRKO_DECIMAL</c>, the text a decimal parameter binds as, ordered by the collation
/// <see cref="SqLiteDecimal"/> registers on every connector connection.
/// </para>
/// </summary>
public class SqLiteDecimalStorageEndToEndTests : IDisposable
{
    private const string TableName = "DecimalLedger";
    private readonly string _root;

    public SqLiteDecimalStorageEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-decimal-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table(TableName)]
    public class Ledger : AbstractModel
    {
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        [PrecisionField(22)]
        [ScaleField(6)]
        public decimal Price { get; set; }
        public decimal? Optional { get; set; }
        public double Ratio { get; set; }
        public int Hits { get; set; }
    }

    /// <summary>The migration's hard case: an inline UNIQUE, a named index on the decimal, and a child FK.</summary>
    [Table("IndexedLedger")]
    public class IndexedLedger : AbstractDatabaseModel
    {
        [UniqueField]
        public string Code { get; set; } = string.Empty;
        [IndexedField("ix_indexed_ledger_amount", 0)]
        public decimal Amount { get; set; }
    }

    private const string DbName = "ledger.db";

    /// <summary>Several statements on ONE connection — the migration's PRAGMAs are per-connection.</summary>
    private void RawBatch(params string[] statements)
    {
        using var connection = new SqliteConnection(Settings.GetConnectionString());
        SqLiteDecimal.Register(connection);
        connection.Open();
        foreach (var sql in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    private SqLiteSettings Settings => new(_root, DbName);

    private SqLiteConnector Connector()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = DbName });
        return (SqLiteConnector)factory.GetConnector();
    }

    private AsyncSQLiteStore<Ledger> Store()
    {
        var store = new AsyncSQLiteStore<Ledger>();
        store.SetSettings(Settings);
        return store;
    }

    private async Task<AsyncSQLiteStore<Ledger>> Seeded(params decimal[] amounts)
    {
        var store = Store();
        foreach (var amount in amounts)
        {
            await store.CreateAsync(new Ledger { Guid = Guid.NewGuid(), Name = amount.ToString(System.Globalization.CultureInfo.InvariantCulture), Amount = amount, Price = amount });
        }
        return store;
    }

    /// <summary>Raw SQL on a connection the test registers itself, so it reads what is on disk.</summary>
    private object? Raw(string sql)
    {
        using var connection = new SqliteConnection(Settings.GetConnectionString());
        SqLiteDecimal.Register(connection);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Fact]
    public async Task A_decimal_column_is_declared_TEXT_with_the_decimal_collation_and_a_double_stays_REAL()
    {
        await Store().CreateAsync(new Ledger { Guid = Guid.NewGuid() });

        var createSql = (string)Raw($"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '{TableName}'")!;
        createSql.Should().Contain("Amount TEXT COLLATE BIRKO_DECIMAL")
            .And.Contain("Price TEXT COLLATE BIRKO_DECIMAL", "declared precision is not rendered: SQLite would ignore it")
            .And.Contain("Optional TEXT COLLATE BIRKO_DECIMAL")
            .And.Contain("Ratio REAL");
        Raw($"SELECT type FROM pragma_table_info('{TableName}') WHERE name = 'Amount'").Should().Be("TEXT",
            "pragma_table_info drops the collation, which is why the drift check reads the CREATE statement");
    }

    [Theory]
    [InlineData("1234567890123456.123456")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("-5.5")]
    [InlineData("0")]
    public async Task Every_decimal_round_trips_exactly(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var store = Store();
        var guid = Guid.NewGuid();
        await store.CreateAsync(new Ledger { Guid = guid, Amount = value, Price = value, Optional = value });

        var row = (await store.ReadAsync(x => x.Guid == guid)).Single();
        row.Amount.Should().Be(value);
        row.Price.Should().Be(value);
        row.Optional.Should().Be(value);
    }

    [Fact]
    public async Task A_null_decimal_stays_null()
    {
        var store = Store();
        var guid = Guid.NewGuid();
        await store.CreateAsync(new Ledger { Guid = guid, Optional = null });

        (await store.ReadAsync(x => x.Guid == guid)).Single().Optional.Should().BeNull();
    }

    [Fact]
    public async Task Ordering_and_range_predicates_are_numeric_not_lexical()
    {
        var store = await Seeded(100m, 9m, -5.5m, 10m, 2.5m, 0.2m, 1234567890123456.123456m);

        (await store.ReadAsync(null, OrderBy<Ledger>.By(x => x.Amount), null, null)).Select(x => x.Amount)
            .Should().Equal(-5.5m, 0.2m, 2.5m, 9m, 10m, 100m, 1234567890123456.123456m);
        (await store.ReadAsync(null, OrderBy<Ledger>.ByDescending(x => x.Price), null, null)).Select(x => x.Price)
            .Should().Equal(1234567890123456.123456m, 100m, 10m, 9m, 2.5m, 0.2m, -5.5m);
        (await store.ReadAsync(x => x.Amount > 9.5m)).Select(x => x.Amount).OrderBy(x => x)
            .Should().Equal(10m, 100m, 1234567890123456.123456m);
        (await store.ReadAsync(x => x.Amount >= -5.5m && x.Amount < 2.5m)).Select(x => x.Amount).OrderBy(x => x)
            .Should().Equal(-5.5m, 0.2m);
    }

    [Fact]
    public async Task Equality_ignores_trailing_zeros()
    {
        var store = await Seeded(10m, 2.5m);

        (await store.ReadAsync(x => x.Amount == 10.00m)).Should().ContainSingle();
        var wanted = new[] { 10.0m, 2.50m };
        (await store.ReadAsync(x => wanted.Contains(x.Amount))).Should().HaveCount(2);
    }

    [Fact]
    public async Task An_increment_is_exact_and_then_matches_by_equality()
    {
        var store = await Seeded(10.10m, 1234567890123456.123456m);

        await store.UpdateAllAsync(new PropertyUpdate<Ledger>().Increment(x => x.Amount, 0.20m).Increment(x => x.Hits, 2));

        (await store.ReadAsync(null, OrderBy<Ledger>.By(x => x.Amount), null, null)).Select(x => (x.Amount, x.Hits))
            .Should().Equal((10.30m, 2), (1234567890123456.323456m, 2));
        (await store.ReadAsync(x => x.Amount == 10.30m)).Should().ContainSingle();
    }

    [Fact]
    public async Task Concurrent_increments_are_neither_lost_nor_drifted()
    {
        var store = await Seeded(0m);
        var guid = (await store.ReadAsync(x => x.Amount == 0m)).Single().Guid!.Value;

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            Store().UpdateAsync(x => x.Guid == guid, new PropertyUpdate<Ledger>().Increment(x => x.Amount, 0.01m))));

        (await store.ReadAsync(x => x.Guid == guid)).Single().Amount.Should().Be(0.50m);
    }

    [Fact]
    public async Task An_increment_past_decimal_MaxValue_is_refused_and_changes_nothing()
    {
        var store = await Seeded(decimal.MaxValue);

        var act = () => store.UpdateAllAsync(new PropertyUpdate<Ledger>().Increment(x => x.Amount, 1m));

        await act.Should().ThrowAsync<Exception>();
        (await store.ReadAsync(null, null, null, null)).Single().Amount.Should().Be(decimal.MaxValue);
    }

    [Fact]
    public async Task DetectDrift_is_clean_on_a_table_the_framework_created()
    {
        await Store().CreateAsync(new Ledger { Guid = Guid.NewGuid() });

        var report = Connector().DetectDrift(typeof(Ledger));

        report.TableExists.Should().BeTrue();
        report.Drifts.Should().BeEmpty();
    }

    /// <summary>
    /// The upgrade case: a table <c>CREATE TABLE</c> made before TASK-513. Reported, never altered (rule 49:
    /// DetectDrift is a diagnostic), with both sides named so the CHANGELOG migration can be checked against it.
    /// </summary>
    [Fact]
    public void DetectDrift_reports_a_decimal_column_created_before_TASK_513()
    {
        Raw($"CREATE TABLE {TableName} (Guid TEXT PRIMARY KEY NOT NULL, Name TEXT, Amount REAL NOT NULL, "
            + "Price NUMERIC(22,6) NOT NULL, Optional TEXT, Ratio REAL NOT NULL, Hits INTEGER NOT NULL)");

        var drifts = Connector().DetectDrift(typeof(Ledger)).Drifts;

        drifts.Select(d => (d.Column, d.Kind, d.Declared, d.Stored)).Should().BeEquivalentTo(new[]
        {
            ("Amount", ColumnDriftKind.TypeMismatch, (string?)"TEXT COLLATE BIRKO_DECIMAL", (string?)"REAL"),
            ("Price", ColumnDriftKind.TypeMismatch, "TEXT COLLATE BIRKO_DECIMAL", "NUMERIC(22,6)"),
            ("Optional", ColumnDriftKind.TypeMismatch, "TEXT COLLATE BIRKO_DECIMAL", "TEXT"),
        }, "a TEXT column without the collation would sort lexically, so it is drift too");
    }

    /// <summary>
    /// The CHANGELOG's migration, statement for statement. Kept in one place so the two tests below cannot drift
    /// from each other, and the CHANGELOG is checked against this method.
    /// </summary>
    private void Migrate(SqLiteConnector connector, Type model, string table, string columns)
    {
        var oldIndexes = new System.Collections.Generic.List<string>();
        using (var connection = new SqliteConnection(Settings.GetConnectionString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = '{table}' AND sql IS NOT NULL";
            using var reader = command.ExecuteReader();
            while (reader.Read()) oldIndexes.Add(reader.GetString(0));
        }

        RawBatch(new[]
            {
                "PRAGMA foreign_keys = OFF",
                "PRAGMA legacy_alter_table = ON",
                $"ALTER TABLE \"{table}\" RENAME TO \"{table}_old\"",
            }
            .Concat(oldIndexes.Select(index => $"DROP INDEX \"{index}\""))
            .ToArray());
        connector.CreateTable(new[] { model });
        RawBatch(
            "PRAGMA foreign_keys = OFF",
            $"INSERT INTO \"{table}\" ({columns}) SELECT {columns} FROM \"{table}_old\"",
            $"DROP TABLE \"{table}_old\"");
    }

    /// <summary>
    /// Indexes and foreign keys survive the migration. Measured against the first recipe (a plain RENAME): index
    /// names are schema-global and stay with the renamed table, so CREATE INDEX IF NOT EXISTS skipped them and
    /// DROP TABLE removed them — the new table had no index and no unique constraint, silently; and RENAME
    /// rewrote the child's REFERENCES to the old name, which DROP then left dangling.
    /// </summary>
    [Fact]
    public void The_CHANGELOG_migration_keeps_indexes_unique_constraints_and_foreign_keys()
    {
        RawBatch(
            "CREATE TABLE IndexedLedger (Guid TEXT PRIMARY KEY NOT NULL, Code TEXT UNIQUE NOT NULL, Amount REAL NOT NULL)",
            "CREATE INDEX ix_indexed_ledger_amount ON IndexedLedger (Amount)",
            "CREATE TABLE LedgerLine (Id INTEGER PRIMARY KEY, LedgerGuid TEXT REFERENCES \"IndexedLedger\" (Guid))",
            $"INSERT INTO IndexedLedger VALUES ('{Guid.NewGuid()}', 'A', 9.5), ('{Guid.NewGuid()}', 'B', 10)",
            "INSERT INTO LedgerLine (LedgerGuid) SELECT Guid FROM IndexedLedger");
        var connector = Connector();

        Migrate(connector, typeof(IndexedLedger), "IndexedLedger", "Guid, Code, Amount");

        connector.DetectDrift(typeof(IndexedLedger)).Drifts.Should().BeEmpty();
        Raw("SELECT group_concat(name, ',') FROM (SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'IndexedLedger' ORDER BY name)")
            .Should().Be("ix_indexed_ledger_amount,sqlite_autoindex_IndexedLedger_1,ux_IndexedLedger_Code");
        FluentActions.Invoking(() => Raw($"INSERT INTO IndexedLedger (Guid, Code, Amount) VALUES ('{Guid.NewGuid()}', 'A', 1)"))
            .Should().Throw<SqliteException>().WithMessage("*UNIQUE*");
        ((string)Raw("SELECT sql FROM sqlite_master WHERE name = 'LedgerLine'")!).Should().Contain("REFERENCES \"IndexedLedger\"");
        Raw("SELECT COUNT(*) FROM pragma_foreign_key_check").Should().Be(0L);
        Raw("SELECT group_concat(Amount, ',') FROM (SELECT Amount FROM IndexedLedger ORDER BY Amount)").Should().Be("9.5,10.0");
    }

    /// <summary>
    /// The CHANGELOG's migration, run as written: rename the old table, let the framework create the new one,
    /// copy the rows across (TEXT affinity converts each REAL / NUMERIC to text), drop the old table.
    /// </summary>
    [Fact]
    public async Task The_CHANGELOG_migration_clears_the_drift_and_keeps_every_value_a_float_held()
    {
        Raw($"CREATE TABLE {TableName} (Guid TEXT PRIMARY KEY NOT NULL, Name TEXT, Amount REAL NOT NULL, "
            + "Price NUMERIC(22,6) NOT NULL, Optional REAL, Ratio REAL NOT NULL, Hits INTEGER NOT NULL)");
        Raw($"INSERT INTO {TableName} VALUES ('{Guid.NewGuid()}', 'a', 10.299999999999999, 9, NULL, 0.5, 1), "
            + $"('{Guid.NewGuid()}', 'b', 100, 2.5, 0.1, 0.5, 2), ('{Guid.NewGuid()}', 'c', -5.5, 1234567.891, 7, 0.5, 3)");
        var connector = Connector();

        Migrate(connector, typeof(Ledger), TableName, "Guid, Name, Amount, Price, Optional, Ratio, Hits");

        connector.DetectDrift(typeof(Ledger)).Drifts.Should().BeEmpty();
        Raw($"SELECT group_concat(typeof(Amount) || ':' || Amount, ' ') FROM (SELECT Amount FROM {TableName} ORDER BY Name)")
            .Should().Be("text:10.299999999999999 text:100.0 text:-5.5",
                "measured: a REAL converts to its shortest round-trip text, so a value that already drifted keeps "
                + "its drift; the migration stops new drift, it does not repair old values");
        var rows = (await Store().ReadAsync(null, OrderBy<Ledger>.By(x => x.Price), null, null)).ToList();
        rows.Select(x => x.Price).Should().Equal(2.5m, 9m, 1234567.891m);
        rows.Select(x => x.Optional).Should().Equal(0.1m, null, 7m);
    }

    [Fact]
    public void EnsureColumns_adds_a_decimal_column_with_the_collation_and_backfills_the_bound_text_form()
    {
        Raw($"CREATE TABLE {TableName} (Guid TEXT PRIMARY KEY NOT NULL, Name TEXT, Optional TEXT COLLATE BIRKO_DECIMAL, "
            + "Ratio REAL NOT NULL, Hits INTEGER NOT NULL)");
        Raw($"INSERT INTO {TableName} (Guid, Name, Ratio, Hits) VALUES ('{Guid.NewGuid()}', 'old', 0, 0)");
        var connector = Connector();

        connector.EnsureColumns(typeof(Ledger)).Select(d => d.Column).Should().BeEquivalentTo("Amount", "Price");

        connector.DetectDrift(typeof(Ledger)).Drifts.Should().BeEmpty();
        Raw($"SELECT Amount FROM {TableName}").Should().Be("0.0", "the text a decimal parameter binds 0m as");
        Raw($"SELECT typeof(Price) FROM {TableName}").Should().Be("text");
    }

    [Theory]
    [InlineData("CREATE TABLE t (Amount TEXT COLLATE BIRKO_DECIMAL NOT NULL)", "Amount", "TEXT", "BIRKO_DECIMAL")]
    [InlineData("CREATE TABLE t (Guid TEXT, \"Amount\" TEXT COLLATE \"birko_decimal\")", "Amount", "TEXT", "BIRKO_DECIMAL")]
    [InlineData("CREATE TABLE t (AmountX TEXT COLLATE BIRKO_DECIMAL, Amount TEXT)", "Amount", "TEXT", null)]
    [InlineData("CREATE TABLE t (XAmount TEXT COLLATE BIRKO_DECIMAL, Amount TEXT)", "Amount", "TEXT", null)]
    [InlineData("CREATE TABLE t (Amount REAL NOT NULL)", "Amount", "REAL", null)]
    [InlineData("CREATE TABLE t (Name TEXT COLLATE NOCASE, Amount TEXT COLLATE BIRKO_DECIMAL)", "Name", "TEXT", "NOCASE")]
    [InlineData("CREATE TABLE t (Amount TEXT NOT NULL DEFAULT ('0.0') COLLATE BIRKO_DECIMAL)", "Amount", "TEXT", "BIRKO_DECIMAL")]
    [InlineData("CREATE TABLE t (Amount TEXT DEFAULT 'a, b' COLLATE BIRKO_DECIMAL)", "Amount", "TEXT", "BIRKO_DECIMAL")]
    [InlineData("CREATE TABLE t (Amount TEXT NOT NULL, Other TEXT COLLATE BIRKO_DECIMAL)", "Amount", "TEXT", null)]
    [InlineData("CREATE TABLE t (Amount NUMERIC(22,6) NOT NULL COLLATE BINARY)", "Amount", "NUMERIC(22,6)", "BINARY")]
    public void The_stored_collation_is_read_from_the_columns_own_definition(string createSql, string column, string type, string? expected)
        => SqLiteConnector.DeclaredCollation(createSql, column, type).Should().Be(expected);
}
