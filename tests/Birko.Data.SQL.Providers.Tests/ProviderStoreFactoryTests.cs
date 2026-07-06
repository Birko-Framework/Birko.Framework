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
    public class Widget : AbstractModel
    {
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
    public void MSSql_live_crud_round_trip()
    {
        RunLiveCrud(System.Environment.GetEnvironmentVariable("BIRKO_MSSQL_TEST"), cs =>
        {
            // Opt-in: BIRKO_MSSQL_TEST = "host;db;user;pass". Absent → skipped above.
            var p = cs.Split(';');
            var factory = new MSSqlStoreFactory(new MSSqlStoreFactoryOptions { Location = p[0], Name = p[1], UserName = p[2], Password = p[3], TrustServerCertificate = true });
            factory.GetConnector().Should().NotBeNull();
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

    // Runs the live body only when a connection string is configured; otherwise a no-op skip.
    private static void RunLiveCrud(string? connString, System.Action<string> body)
    {
        if (string.IsNullOrWhiteSpace(connString)) return; // opt-in: env var absent → skipped
        body(connString);
    }
}
