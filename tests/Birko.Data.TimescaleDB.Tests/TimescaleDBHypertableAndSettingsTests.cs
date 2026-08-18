using System;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.TimescaleDB.Tests;

/// <summary>
/// CR-M177: broadens the thin TimescaleDB coverage — the create_hypertable SQL composition,
/// TimescaleDBSettings.GetConnectionString()/LoadFrom, and the store's SetSettings overload routing
/// + "Connector not initialized" guards (all offline, no live DB).
/// </summary>
public class TimescaleDBHypertableAndSettingsTests
{
    private sealed class Metric : Birko.Data.Models.AbstractModel
    {
        public double Value { get; set; }
    }

    /// <summary>
    /// TASK-253: <c>BuildCreateHypertableSql</c> became an <b>instance</b> method so it can reach
    /// <c>RegclassLiteral</c> / <c>CatalogueNameLiteral</c> on the base, which consult provider state
    /// (<c>QuoteIdentifier</c> and <c>FoldsUnquotedIdentifiers</c>). Constructing a connector costs nothing
    /// offline — the constructor only stores settings and opens no connection — so the four assertions below
    /// are unchanged from when they called it statically.
    /// </summary>
    private static TimescaleDBConnector Emitter()
        => new(new TimescaleDBSettings("localhost", "db", "u", "p", 5432, "ts", "1 day"));

    // ── create_hypertable SQL composition ──

    /// <summary>
    /// <b>This test used to pin the defect, and its choice of table name is why (TASK-472).</b> It asserted
    /// <c>create_hypertable('metrics', …)</c> — an already-lowercase name, for which the missing identifier
    /// quotes make no difference, because PostgreSQL's regclass folding lands on the same relation. Every
    /// real Birko entity is PascalCase, where the bare literal resolved to a relation that does not exist,
    /// raised <c>42P01</c> and was swallowed as "missing table". A fixture that cannot distinguish the fix
    /// from the defect is not coverage; the PascalCase case below is now the load-bearing one.
    /// </summary>
    [Fact]
    public void BuildCreateHypertableSql_ComposesQuotedArgsAndInterval()
    {
        var sql = Emitter().BuildCreateHypertableSql("metrics", "ts", "7 days");

        sql.Should().Be("SELECT create_hypertable('\"metrics\"', 'ts', chunk_time_interval => INTERVAL '7 days', if_not_exists => TRUE)");
    }

    /// <summary>
    /// The table is a <c>regclass</c> and must carry its own quotes to survive folding; the time column is a
    /// <c>name</c> compared against <c>pg_attribute.attname</c> and must be pre-folded, because the framework
    /// emits column definitions bare. Opposite treatments, same root cause: neither travels as an identifier
    /// the parser can fold.
    /// </summary>
    [Fact]
    public void BuildCreateHypertableSql_QuotesThePascalCaseTableAndFoldsTheColumn()
    {
        var sql = Emitter().BuildCreateHypertableSql("SensorReadings", "Ts", "1 day");

        sql.Should().Be("SELECT create_hypertable('\"SensorReadings\"', 'ts', chunk_time_interval => INTERVAL '1 day', if_not_exists => TRUE)");
    }

    [Fact]
    public void BuildCreateHypertableSql_EscapesSingleQuotes()
    {
        var sql = Emitter().BuildCreateHypertableSql("me'tric", "t'c", "1 day");

        sql.Should().Contain("'\"me''tric\"'");
        sql.Should().Contain("'t''c'");
    }

    /// <summary>
    /// The identifier quotes added to the table argument are themselves doubled, so a name carrying a double
    /// quote cannot break out of the quoted identifier and reshape the regclass literal.
    /// </summary>
    [Fact]
    public void BuildCreateHypertableSql_DoublesEmbeddedDoubleQuotesInTheTable()
    {
        var sql = Emitter().BuildCreateHypertableSql("we\"ird", "ts", "1 day");

        sql.Should().Contain("'\"we\"\"ird\"'");
    }

    // ── TimescaleDBSettings ──

    [Fact]
    public void GetConnectionString_IncludesTimeoutsAndSsl()
    {
        var settings = new TimescaleDBSettings("host", "db", "u", "p", 5432)
        {
            ConnectionTimeout = 20,
            CommandTimeout = 99,
            UseSecure = true,
        };

        var cs = settings.GetConnectionString();

        cs.Should().Contain("Host=host");
        cs.Should().Contain("Database=db");
        cs.Should().Contain("Timeout=20");
        cs.Should().Contain("Command Timeout=99");
        cs.Should().Contain("SSL Mode=Require");
    }

    [Fact]
    public void GetConnectionString_NoSslWhenNotSecure()
    {
        var settings = new TimescaleDBSettings("host", "db", "u", "p", 5432) { UseSecure = false };

        settings.GetConnectionString().Should().NotContain("SSL Mode");
    }

    [Fact]
    public void LoadFrom_CopiesHypertableAndConnectionFields()
    {
        var source = new TimescaleDBSettings("host", "db", "u", "p", 5432, timeColumn: "event_time", chunkTimeInterval: "1 day")
        {
            CommandTimeout = 42,
        };
        var target = new TimescaleDBSettings();

        target.LoadFrom(source);

        target.TimeColumn.Should().Be("event_time");
        target.ChunkTimeInterval.Should().Be("1 day");
        target.Location.Should().Be("host");
        target.CommandTimeout.Should().Be(42);
    }

    [Fact]
    public void Settings_Defaults_TimeColumnAndChunkInterval()
    {
        var settings = new TimescaleDBSettings();

        settings.TimeColumn.Should().Be("timestamp");
        settings.ChunkTimeInterval.Should().Be("7 days");
    }

    // ── AsyncTimescaleDBStore SetSettings routing + guards ──

    [Fact]
    public void SetSettings_TimescaleSettings_CreatesTimescaleConnector()
    {
        var store = new AsyncTimescaleDBStore<Metric>();
        store.SetSettings(new TimescaleDBSettings("host", "db", "u", "p", 5432));

        store.Connector.Should().NotBeNull();
        store.Connector.Should().BeOfType<TimescaleDBConnector>();
    }

    [Fact]
    public void SetSettings_RemoteSettings_CreatesConnector()
    {
        var store = new AsyncTimescaleDBStore<Metric>();
        store.SetSettings(new Birko.Configuration.RemoteSettings("host", "db", "u", "p", 5432));

        store.Connector.Should().NotBeNull();
    }

    [Fact]
    public async Task LifecycleMethods_WithoutSetSettings_ThrowInvalidOperation()
    {
        var store = new AsyncTimescaleDBStore<Metric>();

        await store.Invoking(s => s.CreateSchemaAsync()).Should().ThrowAsync<InvalidOperationException>();
        await store.Invoking(s => s.DropAsync()).Should().ThrowAsync<InvalidOperationException>();
        await store.Invoking(s => s.CreateHypertableAsync("ts")).Should().ThrowAsync<InvalidOperationException>();
    }
}
