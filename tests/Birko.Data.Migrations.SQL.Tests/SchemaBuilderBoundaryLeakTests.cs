using System;
using System.IO;
using System.Linq;
using Birko.Data.Migrations;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.SQL;
using Birko.Data.Migrations.SQL.Settings;
using Birko.Data.Models;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

public class SchemaBuilderBoundaryLeakTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private readonly ITestOutputHelper _out;

    public SchemaBuilderBoundaryLeakTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), $"birko-leak-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "leak.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>
    /// Derives from <c>AbstractModel</c> (Guid only), not <c>AbstractLogModel</c>, so the entity's columns
    /// match what the migration below declares. With the log base the migration would create a table lacking
    /// CreatedAt/UpdatedAt, `CREATE TABLE IF NOT EXISTS` would not add them, and the store's SELECT would
    /// fail on a missing column — a fixture defect that looks exactly like the one under test.
    /// </summary>
    [Table("LeakWidgets")]
    public class LeakWidget : AbstractModel
    {
        [MaxLengthField(64)]
        public string Name { get; set; } = null!;
    }

    private sealed class SchemaMigration : AbstractMigration
    {
        public override long Version => 1;
        public override string Name => "CreateLeakWidgets";
        public override void Up(IMigrationContext context)
            => context.Schema.CreateCollection("LeakWidgets")
                .WithField("Guid", FieldType.Guid, isPrimary: true)
                .WithField("Name", FieldType.String, maxLength: 64)
                .Build();
    }

    /// <summary>
    /// TASK-259 — a migration must not leave its connection and transaction on the process-wide cached
    /// connector. `SqlSchemaBuilder` published them via `SetExternalTransaction` at three sites and never
    /// cleared them, and `AbstractConnector` prefers that pair over opening its own connection.
    /// </summary>
    /// <remarks>
    /// Asserted on the connector's own state AND on a store's behaviour afterwards, because the two fail
    /// differently and the second is what a consumer actually sees. Against the pre-fix code the store call
    /// threw with the lazy schema-ensure DDL as its message — and since a store whose schema-ensure throws is
    /// left permanently uninitialised, every later read and write on that entity threw too.
    /// <para>
    /// `UseTransaction` is left at its default (true) deliberately: that is the configuration the defect
    /// needs, and it is the default. Both shipped consumers set false for unrelated reasons, which is the only
    /// reason this was never seen in production.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_migration_does_not_leave_its_transaction_on_the_cached_connector()
    {
        var settings = new SqLiteSettings(_dir, Path.GetFileName(_dbPath));
        var connector = DataBase.GetConnector<SqLiteConnector>(settings);

        AmbientSqlTransaction.Find(settings.GetId()).Should().BeNull("nothing has published a boundary yet");

        var runner = new SqlMigrationRunner(connector);   // default UseTransaction = true
        runner.RegisterMigrations(new SchemaMigration());
        runner.Initialize();
        runner.Migrate().Success.Should().BeTrue("the migration itself must still work");

        // The leak, directly. TASK-259 deleted the ExternalConnection/ExternalTransaction properties this
        // used to read, so the assertion is now on the mechanism that replaced them: a boundary must not
        // survive the flow that entered it.
        AmbientSqlTransaction.Find(settings.GetId()).Should().BeNull(
            "the migration's boundary must not outlive the migration -- the runner disposed both the "
          + "connection and the transaction on the way out");

        // The consequence, as a consumer meets it: a store on the same cached connector must work. Its
        // FIRST act is the lazy schema-ensure, which is what took the stale branch and threw.
        var store = new SQLiteStore<LeakWidget>();
        store.SetSettings(settings);

        var read = () => store.Read().ToList();
        read.Should().NotThrow(
            "a store sharing the cached connector must not inherit the migration's dead connection; "
          + "before TASK-259 this threw with the CREATE TABLE IF NOT EXISTS statement as its message, and "
          + "the store then stayed uninitialised for the life of the process");
    }

    /// <summary>
    /// The `UseTransaction = false` path must keep behaving as it did: with no transaction there is no
    /// boundary to publish, the connector uses its own connection, and nothing is left behind. Both shipped
    /// consumers run this way, so this is the no-regression pin for every production migration today.
    /// </summary>
    [Fact]
    public void Without_a_runner_transaction_nothing_is_published_either()
    {
        var settings = new SqLiteSettings(_dir, Path.GetFileName(_dbPath));
        var connector = DataBase.GetConnector<SqLiteConnector>(settings);

        var runner = new SqlMigrationRunner(connector, new SqlMigrationSettings { UseTransaction = false });
        runner.RegisterMigrations(new SchemaMigration());
        runner.Initialize();
        runner.Migrate().Success.Should().BeTrue();

        AmbientSqlTransaction.Find(settings.GetId()).Should().BeNull();

        var store = new SQLiteStore<LeakWidget>();
        store.SetSettings(settings);
        store.Read().Should().NotBeNull();
    }
}
