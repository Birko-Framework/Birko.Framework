using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.MSSql;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Providers.Tests;

/// <summary>
/// Cross-provider store factories (EPIC-016 / TASK-042), on the TASK-988 factories since TASK-1001 removed the older
/// ones and their DI registration. Construction / settings / connection-string checks run offline (no server). A live CRUD round-trip per
/// provider is opt-in via the family's own <c>BIRKO_{PROVIDER}_HOST</c> gate (<c>BIRKO_MSSQL_HOST</c>,
/// <c>BIRKO_MYSQL_HOST</c>, <c>BIRKO_PG_HOST</c>) and skipped when it is absent.
/// </summary>
public class ProviderStoreFactoryTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ProviderStoreFactoryTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    public class Widget : AbstractModel
    {
        public string? Name { get; set; }
    }

    /// <summary>
    /// The round-trip entity: mapped, unlike <see cref="Widget"/>, because a store cannot create a
    /// table for a type the mapper cannot see — and a factory that hands back a store is worth
    /// nothing if that store cannot reach the database.
    /// </summary>
    [Birko.Data.SQL.Attributes.Table("TASK042_RoundTrip")]
    public class RoundTripRow : AbstractDatabaseModel
    {
        [Birko.Data.SQL.Attributes.MaxLengthField(64)]
        public string? Name { get; set; }
    }

    // ── MSSql ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MSSql_factory_builds_settings_and_connection_string()
    {
        var factory = new MSSqlSqlStoreFactory(new MSSqlSettings("db.example.net", "AppDb", "sa", "pw", 1433, true)
        {
            MultipleActiveResultSets = true, TrustServerCertificate = true,
        });

        // The provider's settings reach the connector whole, not narrowed to PasswordSettings.
        var settings = factory.GetConnector().Settings.Should().BeOfType<MSSqlSettings>().Which;
        settings.Location.Should().Be("db.example.net");
        settings.Name.Should().Be("AppDb");
        settings.MultipleActiveResultSets.Should().BeTrue();
        var cs = settings.GetConnectionString();
        cs.Should().Contain("db.example.net").And.Contain("Initial Catalog=AppDb").And.Contain("Multiple Active Result Sets=True");
        factory.GetAsyncStore<Widget>().Should().BeOfType<AsyncMSSqlStore<Widget>>();
    }

    [Fact]
    public void MSSqlStore_SetSettings_retains_full_connection_fields()
    {
        // Regression for TASK-051: the sync store used to narrow settings to PasswordSettings,
        // dropping UserName / MARS. It must now keep the full MSSqlSettings.
        var store = new MSSqlStore<Widget>();
        store.SetSettings(new MSSqlSettings("regr.example.net", "RegrDb", "regruser", "pw", 1433, true)
        {
            MultipleActiveResultSets = true,
        });

        // Under the old lossy code the connector held a plain PasswordSettings (this cast would fail);
        // the fix keeps the full MSSqlSettings.
        var settings = store.Connector.Settings as MSSqlSettings;
        settings.Should().NotBeNull("the store must retain the full MSSqlSettings, not a narrowed PasswordSettings");
        var cs = settings!.GetConnectionString();
        cs.Should().Contain("User ID=regruser", "UserName must survive SetSettings");
        cs.Should().Contain("Multiple Active Result Sets=True", "SQL Server flags must survive SetSettings");
    }

    [Fact]
    public async Task MSSql_live_crud_round_trip()
    {
        var server = Resolve("MSSQL", 1433, "sa", "Birko!Passw0rd");
        if (server is null)
        {
            return;
        }

        var factory = new MSSqlSqlStoreFactory(new MSSqlSettings(server.Host, server.Database, server.User, server.Password, server.Port, true)
        {
            TrustServerCertificate = true,
        });
        await RoundTripAsync(factory.GetAsyncStore<RoundTripRow>());
    }

    // ── MySQL ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MySql_factory_builds_settings_and_carries_batch_size()
    {
        var factory = new MySQLSqlStoreFactory(new MySqlSettings("mysql.local", "AppDb", "root", "pw", 3306, false)
        {
            BulkInsertBatchSize = 500,
        });

        var settings = factory.GetConnector().Settings.Should().BeOfType<MySqlSettings>().Which;
        settings.Location.Should().Be("mysql.local");
        settings.BulkInsertBatchSize.Should().Be(500);
        settings.GetConnectionString().Should().Contain("AppDb");
        factory.GetAsyncStore<Widget>().Should().BeOfType<AsyncMySQLStore<Widget>>();
    }

    [Fact]
    public async Task MySql_live_crud_round_trip()
    {
        var server = Resolve("MYSQL", 3306, "root", "root");
        if (server is null)
        {
            return;
        }

        var factory = new MySQLSqlStoreFactory(new MySqlSettings(server.Host, server.Database, server.User, server.Password, server.Port, false));
        await RoundTripAsync(factory.GetAsyncStore<RoundTripRow>());
    }

    // ── PostgreSQL ──────────────────────────────────────────────────────────────

    [Fact]
    public void Postgres_factory_builds_settings_and_carries_binary_import()
    {
        var factory = new PostgreSQLSqlStoreFactory(new PostgreSqlSettings("pg.local", "AppDb", "postgres", "pw", 5432, false)
        {
            UseBinaryImport = false,
        });

        var settings = factory.GetConnector().Settings.Should().BeOfType<PostgreSqlSettings>().Which;
        settings.Location.Should().Be("pg.local");
        settings.UseBinaryImport.Should().BeFalse();
        settings.GetConnectionString().Should().Contain("AppDb");
        factory.GetAsyncStore<Widget>().Should().BeOfType<AsyncPostgreSQLStore<Widget>>();
    }

    [Fact]
    public async Task PostgreSql_live_crud_round_trip()
    {
        var server = Resolve("PG", 5432, "postgres", "postgres");
        if (server is null)
        {
            return;
        }

        var factory = new PostgreSQLSqlStoreFactory(new PostgreSqlSettings(server.Host, server.Database, server.User, server.Password, server.Port, false));
        await RoundTripAsync(factory.GetAsyncStore<RoundTripRow>());
    }

    /// <summary>One provider's live server, as the surrounding suites describe theirs.</summary>
    private sealed record LiveServer(string Host, int Port, string User, string Password, string Database);

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    /// <summary>
    /// Resolves the <c>BIRKO_{prefix}_HOST</c> / <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> /
    /// <c>_DB</c> group, or returns <c>null</c> after reporting a skip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ This suite used to gate on a <c>BIRKO_{PROVIDER}_TEST=host;db;user;pass</c> variable of its
    /// own invention — a second gating convention for one suite, where the other eleven SQL suites read
    /// the per-field group above. CI sets the group, so the three round-trips never found a server, and
    /// because <c>BIRKO_REQUIRE_LIVE</c> is on workflow-wide they failed rather than skipped: 3 of 10 red
    /// on every live-tests run from the day they were written. The fix is to read what the fixture
    /// already sets, not to teach the fixture a second vocabulary.
    /// </para>
    /// <para>
    /// The defaults match the containers in <c>.github/workflows/live-tests.yml</c>, exactly as the
    /// sibling suites' defaults do — <c>_HOST</c> alone is what opts a run in, and a developer who
    /// exports it gets the rest for free.
    /// </para>
    /// <para>
    /// The skip is written to the test output rather than returned silently: a test that passes without
    /// running is indistinguishable from one that ran and proved something. <c>BIRKO_REQUIRE_LIVE</c>
    /// turns the skip into a failure, so a CI job whose containers never came up cannot report success.
    /// </para>
    /// </remarks>
    private LiveServer? Resolve(string prefix, int defaultPort, string defaultUser, string defaultPassword)
    {
        var host = Environment.GetEnvironmentVariable($"BIRKO_{prefix}_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            var message = $"SKIPPED: no live server. Set BIRKO_{prefix}_HOST to exercise this test; "
                        + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (RequireLive)
            {
                throw new InvalidOperationException(message);
            }
            return null;
        }

        return new LiveServer(
            host,
            int.TryParse(Environment.GetEnvironmentVariable($"BIRKO_{prefix}_PORT"), out var port) ? port : defaultPort,
            Environment.GetEnvironmentVariable($"BIRKO_{prefix}_USER") ?? defaultUser,
            Environment.GetEnvironmentVariable($"BIRKO_{prefix}_PASSWORD") ?? defaultPassword,
            LiveDatabase.Name(prefix));
    }

    /// <summary>
    /// A real round-trip: create the table, write a row, read it back **by value**, then delete it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the acceptance criterion asked for and what the previous version did not do. It
    /// asserted <c>factory.GetConnector().Should().NotBeNull()</c> — constructing an object, opening
    /// no connection, touching no server. It passed with every database on the machine stopped, which
    /// is how a criterion reading "live CRUD round-trip" stayed ticked for eleven weeks without one
    /// ever happening. MySQL and PostgreSQL had no live test at all.
    /// </para>
    /// <para>
    /// Reading back by <c>Name</c> rather than by the returned id is deliberate: an id that
    /// round-trips only proves the id was echoed back, while a filter forces the value through the
    /// provider's own parameter binding and out again through its reader — which is where the
    /// per-provider column typing this factory selects actually shows up.
    /// </para>
    /// </remarks>
    private static async Task RoundTripAsync(dynamic store)
    {
        var marker = $"task042-{Guid.NewGuid():N}";
        Expression<Func<RoundTripRow, bool>> mine = r => r.Name == marker;
        try
        {
            Guid id = await store.CreateAsync(new RoundTripRow { Name = marker });
            id.Should().NotBe(Guid.Empty, "create must return the row's id");

            long count = await store.CountAsync(mine);
            count.Should().Be(1, "the row must be readable back from the server, not merely written");

            await store.DeleteAsync(mine);
            long after = await store.CountAsync(mine);
            after.Should().Be(0, "delete must remove it — otherwise every run leaves a row behind");
        }
        finally
        {
            // Best effort: a failed assertion above must not leave the marker row on a shared server.
            try { await store.DeleteAsync(mine); }
            catch { /* the assertion is the interesting failure, not the cleanup */ }
        }
    }
}
