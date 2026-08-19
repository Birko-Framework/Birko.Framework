using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-263 — the <b>fallback contract</b> for <c>[UtcField]</c> on a provider with no timezone-aware column
/// type. This is criterion 3: what SQLite stores and returns must be a stated, tested contract, because a
/// silent degrade to a timezone-less column is the failure mode this whole family of findings is about.
///
/// <para>
/// <b>The contract.</b> <c>[UtcField]</c> promises the <b>instant</b> survives exactly and reads back as
/// <c>DateTimeKind.Utc</c> — on every provider, including this one. It does <b>not</b> promise a caller's
/// original offset survives; that is normalised away everywhere, deliberately and uniformly, because a field
/// cannot behave differently per provider (<c>Tables.Table</c> holds no connector and
/// <c>AbstractField.Read</c> is reached through the provider-blind <c>DataBase.Read</c>). Uniformity is the
/// point: this is the provider the product's own tests run on, so a behaviour that differed here from
/// PostgreSQL would make a green test meaningless — which is precisely the trade TASK-256 recorded when it
/// rejected mapping every <c>DateTime</c> to <c>TIMESTAMPTZ</c>.
/// </para>
///
/// <para>
/// <b>What SQLite actually does, measured.</b> Its <c>ConvertType</c> groups <c>DbType.DateTimeOffset</c> with
/// the integral types and declares <c>INTEGER</c> — but SQLite's type affinity is a preference, not a
/// constraint, so Microsoft.Data.Sqlite stores the value as ISO-8601 <i>text carrying the offset</i>
/// (<c>2026-03-15 10:30:00+00:00</c>). The declaration is therefore misleading and the storage is fine. That
/// mismatch is pre-existing and shared with plain <c>DbType.DateTime</c>, which declares <c>INTEGER</c> and
/// also stores text — so it is recorded here rather than "fixed" into a divergence from its neighbour.
/// </para>
///
/// <para>No server required — SQLite is a file.</para>
/// </summary>
public class UtcFieldFallbackTests : IDisposable
{
    private const string TableName = "UtcFallbackRows";

    private static readonly DateTime Utc = new(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);

    private readonly string _root;
    private static int _seq;

    public UtcFieldFallbackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "birko-utcfield-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Table(TableName)]
    public class StampRow : AbstractModel
    {
        [UtcField]
        public DateTime ObservedAt { get; set; }

        public DateTime NoticeDate { get; set; }
    }

    /// <summary>A fresh file per test, so the process-wide connector cache hands out a distinct connector.</summary>
    private SqLiteSettings NewDatabase()
    {
        var settings = new SqLiteSettings(_root, $"utcfield{Interlocked.Increment(ref _seq)}.db");
        new SqLiteConnector(settings).CreateTable(new[] { typeof(StampRow) });
        return settings;
    }

    private static AsyncSQLiteStore<StampRow> AsyncStore(SqLiteSettings settings)
    {
        var store = new AsyncSQLiteStore<StampRow>();
        store.SetSettings(settings);
        return store;
    }

    private static StampRow Row() => new()
    {
        Guid = System.Guid.NewGuid(),
        ObservedAt = Utc,
        NoticeDate = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc),
    };

    private static string Scalar(SqLiteSettings settings, string sql)
    {
        using var conn = new SqliteConnection(settings.GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString() ?? "<null>";
    }

    // ============================================================ the promise that IS kept

    [Fact]
    public async Task The_instant_survives_exactly_and_reads_back_as_utc()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);

        await store.CreateAsync(Row(), null, CancellationToken.None);
        var read = (await store.ReadAsync(CancellationToken.None)).Single();

        read.ObservedAt.Should().Be(Utc,
            "the instant is the promise, and it must hold on the provider with no tz-aware column type — this "
          + "is what makes a SQLite-green test meaningful about PostgreSQL behaviour");
        read.ObservedAt.Kind.Should().Be(DateTimeKind.Utc,
            "read-back is UTC-kinded on every provider, so consuming code needs no per-provider branch");
    }

    [Fact]
    public async Task A_plain_datetime_beside_it_is_still_a_wall_clock()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);

        await store.CreateAsync(Row(), null, CancellationToken.None);
        var read = (await store.ReadAsync(CancellationToken.None)).Single();

        read.NoticeDate.Kind.Should().Be(DateTimeKind.Unspecified,
            "TASK-256's rule is untouched for an unmarked property, here as everywhere");
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task A_value_with_or_without_a_kind_stores_the_same_instant(DateTimeKind kind)
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);
        var row = Row();
        row.ObservedAt = DateTime.SpecifyKind(new DateTime(2026, 3, 15, 10, 30, 0), kind);

        await store.CreateAsync(row, null, CancellationToken.None);
        var read = (await store.ReadAsync(CancellationToken.None)).Single();

        read.ObservedAt.Should().Be(Utc,
            "[UtcField] declares the property holds UTC, so an Unspecified value is taken at its word rather "
          + "than being read as local — otherwise the stored instant would depend on the writing machine");
    }

    // ============================================================ the promise that is NOT kept

    /// <summary>
    /// The offset loss, asserted rather than implied (criterion 3). A caller who sets a <c>Kind=Local</c> value
    /// gets the right <i>instant</i> back, but as UTC — the original wall clock and offset are gone. If this
    /// ever starts returning <c>+01:00</c>, the contract has silently widened and the docs are wrong.
    /// </summary>
    [Fact]
    public async Task The_original_offset_is_normalised_away_not_preserved()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);
        var local = new DateTime(2026, 3, 15, 11, 30, 0, DateTimeKind.Local);
        var row = Row();
        row.ObservedAt = local;

        await store.CreateAsync(row, null, CancellationToken.None);
        var read = (await store.ReadAsync(CancellationToken.None)).Single();

        read.ObservedAt.Should().Be(local.ToUniversalTime(), "the instant is preserved");
        read.ObservedAt.Kind.Should().Be(DateTimeKind.Utc,
            "and it comes back as UTC, not as the Local it went in as — the offset is normalised away");
    }

    // ============================================================ what the storage actually looks like

    /// <summary>
    /// Records the declared-type / stored-form mismatch rather than pretending it away. Pinned so that a later
    /// change to SQLite's <c>ConvertType</c> — or to Microsoft.Data.Sqlite's conversion — surfaces here instead
    /// of as a wrong instant somewhere downstream.
    /// </summary>
    [Fact]
    public async Task The_column_declares_INTEGER_while_storing_ISO_text_with_an_offset()
    {
        var settings = NewDatabase();
        await AsyncStore(settings).CreateAsync(Row(), null, CancellationToken.None);

        var declared = Scalar(settings,
            $"SELECT type FROM pragma_table_info('{TableName}') WHERE name = 'ObservedAt'");
        var storedType = Scalar(settings, $"SELECT typeof(ObservedAt) FROM \"{TableName}\" LIMIT 1");
        var stored = Scalar(settings, $"SELECT ObservedAt FROM \"{TableName}\" LIMIT 1");

        declared.Should().Be("INTEGER",
            "SQLite's ConvertType groups DbType.DateTimeOffset with the integral types. Misleading, and "
          + "deliberately left alone: plain DbType.DateTime declares INTEGER and stores text too, so changing "
          + "only this one would make it diverge from its neighbour");
        storedType.Should().Be("text",
            "type affinity is a preference, not a constraint — the driver writes ISO-8601 text");
        stored.Should().StartWith("2026-03-15 10:30:00",
            "the UTC instant, in text; the +00:00 suffix is what lets the read reconstruct it exactly");
    }
}
