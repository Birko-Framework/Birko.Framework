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
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Data.SQL.Providers.Tests;

/// <summary>
/// Cross-provider store-factory + DI backport (EPIC-016 / TASK-042). Construction / settings /
/// connection-string / DI-registration checks run offline (no server). A live CRUD round-trip per
/// provider is opt-in via an env var (e.g. <c>BIRKO_MSSQL_TEST</c>) and skipped when it is absent.
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
        var factory = new MSSqlStoreFactory(new MSSqlStoreFactoryOptions
        {
            Location = "db.example.net", Name = "AppDb", UserName = "sa", Password = "pw",
            Port = 1433, MultipleActiveResultSets = true, TrustServerCertificate = true,
        });

        factory.Settings.Location.Should().Be("db.example.net");
        factory.Settings.Name.Should().Be("AppDb");
        factory.Settings.MultipleActiveResultSets.Should().BeTrue();
        var cs = factory.Settings.GetConnectionString();
        cs.Should().Contain("db.example.net").And.Contain("Initial Catalog=AppDb").And.Contain("MultipleActiveResultSets=True");
        factory.GetAsyncStore<Widget>().Should().NotBeNull();
        factory.GetConnector().Should().NotBeNull();
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
        cs.Should().Contain("MultipleActiveResultSets=True", "SQL Server flags must survive SetSettings");
    }

    [Fact]
    public void AddMSSqlStores_registers_a_resolvable_singleton()
    {
        var services = new ServiceCollection();
        services.AddMSSqlStores(o => { o.Location = "h"; o.Name = "d"; o.UserName = "u"; o.Password = "p"; });
        using var provider = services.BuildServiceProvider();

        var a = provider.GetService<IMSSqlStoreFactory>();
        a.Should().NotBeNull();
        a.Should().BeSameAs(provider.GetService<IMSSqlStoreFactory>()); // singleton
        a!.Settings.Name.Should().Be("d");
    }

    [Fact]
    public async Task MSSql_live_crud_round_trip()
    {
        await RunLiveAsync("BIRKO_MSSQL_TEST", async p =>
        {
            var factory = new MSSqlStoreFactory(new MSSqlStoreFactoryOptions
            {
                Location = p[0], Name = p[1], UserName = p[2], Password = p[3], TrustServerCertificate = true,
            });
            await RoundTripAsync(factory.GetAsyncStore<RoundTripRow>());
        });
    }

    // ── MySQL ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MySql_factory_builds_settings_and_carries_batch_size()
    {
        var factory = new MySQLStoreFactory(new MySQLStoreFactoryOptions
        {
            Location = "mysql.local", Name = "AppDb", UserName = "root", Password = "pw", BulkInsertBatchSize = 500,
        });

        factory.Settings.Location.Should().Be("mysql.local");
        factory.Settings.BulkInsertBatchSize.Should().Be(500);
        factory.Settings.GetConnectionString().Should().Contain("AppDb");
        factory.GetAsyncStore<Widget>().Should().NotBeNull();
        factory.GetConnector().Should().NotBeNull();
    }

    [Fact]
    public async Task MySql_live_crud_round_trip()
    {
        await RunLiveAsync("BIRKO_MYSQL_TEST", async p =>
        {
            var factory = new MySQLStoreFactory(new MySQLStoreFactoryOptions
            {
                Location = p[0], Name = p[1], UserName = p[2], Password = p[3],
            });
            await RoundTripAsync(factory.GetAsyncStore<RoundTripRow>());
        });
    }

    [Fact]
    public void AddMySqlStores_registers_a_resolvable_singleton()
    {
        var services = new ServiceCollection();
        services.AddMySqlStores(o => { o.Location = "h"; o.Name = "d"; o.UserName = "u"; o.Password = "p"; });
        using var provider = services.BuildServiceProvider();

        var a = provider.GetService<IMySQLStoreFactory>();
        a.Should().NotBeNull();
        a.Should().BeSameAs(provider.GetService<IMySQLStoreFactory>());
        a!.Settings.Name.Should().Be("d");
    }

    // ── PostgreSQL ──────────────────────────────────────────────────────────────

    [Fact]
    public void Postgres_factory_builds_settings_and_carries_binary_import()
    {
        var factory = new PostgreSQLStoreFactory(new PostgreSQLStoreFactoryOptions
        {
            Location = "pg.local", Name = "AppDb", UserName = "postgres", Password = "pw", UseBinaryImport = false,
        });

        factory.Settings.Location.Should().Be("pg.local");
        factory.Settings.UseBinaryImport.Should().BeFalse();
        factory.Settings.GetConnectionString().Should().Contain("AppDb");
        factory.GetAsyncStore<Widget>().Should().NotBeNull();
        factory.GetConnector().Should().NotBeNull();
    }

    [Fact]
    public async Task PostgreSql_live_crud_round_trip()
    {
        await RunLiveAsync("BIRKO_POSTGRES_TEST", async p =>
        {
            var factory = new PostgreSQLStoreFactory(new PostgreSQLStoreFactoryOptions
            {
                Location = p[0], Name = p[1], UserName = p[2], Password = p[3],
            });
            await RoundTripAsync(factory.GetAsyncStore<RoundTripRow>());
        });
    }

    [Fact]
    public void AddPostgreSqlStores_registers_a_resolvable_singleton()
    {
        var services = new ServiceCollection();
        services.AddPostgreSqlStores(o => { o.Location = "h"; o.Name = "d"; o.UserName = "u"; o.Password = "p"; });
        using var provider = services.BuildServiceProvider();

        var a = provider.GetService<IPostgreSQLStoreFactory>();
        a.Should().NotBeNull();
        a.Should().BeSameAs(provider.GetService<IPostgreSQLStoreFactory>());
        a!.Settings.Name.Should().Be("d");
    }

    /// <summary>
    /// Runs <paramref name="body"/> against the server described by <paramref name="envVar"/>
    /// (<c>host;db;user;pass</c>), or reports a skip.
    /// </summary>
    /// <remarks>
    /// ⚠ The previous version returned **silently** when the variable was absent, so the suite
    /// reported every test passed whether a server had been involved or not — and a test that passes
    /// without running is indistinguishable from one that ran and proved something. The skip is now
    /// written to the test output, and <c>BIRKO_REQUIRE_LIVE</c> turns it into a failure, which is
    /// the convention the framework's other live suites already use: without it a CI job whose
    /// containers never came up reports success.
    /// </remarks>
    private async Task RunLiveAsync(string envVar, Func<string[], Task> body)
    {
        var connString = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(connString))
        {
            var message = $"SKIPPED: no live server. Set {envVar}=host;db;user;pass to exercise this "
                        + "test; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            {
                throw new InvalidOperationException(message);
            }
            return;
        }

        var parts = connString.Split(';');
        parts.Length.Should().BeGreaterThanOrEqualTo(4, $"{envVar} must be host;db;user;pass");
        await body(parts);
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
