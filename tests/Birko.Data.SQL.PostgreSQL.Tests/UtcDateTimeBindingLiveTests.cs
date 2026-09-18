using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// What a Birko <c>DateTime</c> column means on PostgreSQL, and that both write paths agree about it
/// (TASK-256).
///
/// <para>
/// <b>The rule under test.</b> <c>ConvertType</c> maps <c>DbType.DateTime</c> to <c>TIMESTAMP</c> — without
/// time zone — so the column stores the <b>wall-clock components of the value as supplied</b>.
/// <c>DateTimeKind</c> is not persisted and every read returns <c>Unspecified</c>; re-attaching the Kind is
/// the caller's job. Both write paths therefore strip Kind before binding, which makes the stored value
/// independent of the server's <c>TimeZone</c> setting.
/// </para>
///
/// <para>
/// <b>Why there were two defects and only one was visible.</b> The binary <c>COPY</c> writer passes
/// <c>NpgsqlDbType.Timestamp</c> explicitly and Npgsql <i>refuses</i> a <c>Kind=Utc</c> value — loud, and it
/// blocked <c>CreateManyAsync</c> for every entity descending from the framework's own
/// <c>AbstractLogModel</c> (which initialises <c>CreatedAt</c>/<c>UpdatedAt</c> from
/// <see cref="DateTime.UtcNow"/>). <c>AddParameter</c> binds no <c>DbType</c>, so Npgsql infers
/// <c>timestamptz</c> for the same value and the server casts it into the timezone-less column <b>using the
/// session's TimeZone</b> — silently storing a shifted instant. Measured: a 10:30 UTC value stored as 11:30
/// on a UTC+1 server, no error.
/// </para>
///
/// <para>
/// <b>Read <see cref="Parameterised_and_bulk_agree_on_a_non_utc_server"/> before changing anything here.</b>
/// On a UTC server the parameterised half of the fix is <i>unobservable</i> — both paths store 10:30 either
/// way — so that test is the only thing that can tell the fix from a no-op, and it is why this suite stands
/// up a database of its own with a non-UTC <c>TimeZone</c>.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_PG_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>), and a
/// skipped run says so out loud — see <see cref="RequireServer"/>.
/// </para>
/// </summary>
public class UtcDateTimeBindingLiveTests : IDisposable
{
    private const string TableName = "UtcBindRows";

    /// <summary>The value every test writes: 10:30 UTC, chosen so a UTC+1 shift is unmistakable (11:30).</summary>
    private static readonly DateTime Utc = new(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);

    /// <summary>The same wall clock with no Kind — what the column is expected to hold, on any server.</summary>
    private static readonly DateTime ExpectedWallClock = new(2026, 3, 15, 10, 30, 0, DateTimeKind.Unspecified);

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public UtcDateTimeBindingLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            return true;
        }
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return false;
    }

    private static PostgreSqlSettings Settings(string? database = null)
        => new(Host!, database ?? Database, User, Password) { Port = Port };

    public class StampRow : AbstractModel
    {
        public string? Name { get; set; }
        public DateTime Ts { get; set; }
    }

    private sealed class StampRowMapping : IModelMapping<StampRow>
    {
        public void Configure(ModelMap<StampRow> map)
        {
            map.ToTable(TableName).HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Ts);
        }
    }

    private static void Exec(string sql, string? database = null)
    {
        using var conn = new NpgsqlConnection(Settings(database).GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// The stored text, read on a connection of its own so the answer is what is <b>committed</b> and is not
    /// filtered through the framework's own read path. <c>::text</c> rather than a typed read, because the
    /// question is what the column holds — a typed read would re-apply Npgsql's conversions.
    /// </summary>
    private static string StoredText(string name, string? database = null)
    {
        using var conn = new NpgsqlConnection(Settings(database).GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Columns bare: the base-table DDL emits them unquoted, so PostgreSQL folds them and a quoted
        // "Ts" resolves to nothing (42703).
        cmd.CommandText = $"SELECT ts::text FROM \"{TableName}\" WHERE name = @n";
        var p = cmd.CreateParameter();
        p.ParameterName = "@n";
        p.Value = name;
        cmd.Parameters.Add(p);
        return cmd.ExecuteScalar()?.ToString() ?? "<null>";
    }

    private static string ColumnType(string? database = null)
    {
        using var conn = new NpgsqlConnection(Settings(database).GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        // The table name keeps its case and the column does not: CreateTable QUOTES the table identifier
        // (so the catalogue holds "UtcBindRows") and emits column definitions BARE (so PostgreSQL folds them
        // to 'ts'). Lowercasing both is how this lookup first returned <none> — the same trap TASK-253 hit
        // against timescaledb_information.hypertables.
        cmd.CommandText = "SELECT data_type FROM information_schema.columns "
                        + $"WHERE table_name = '{TableName}' AND column_name = 'ts'";
        return cmd.ExecuteScalar()?.ToString() ?? "<none>";
    }

    private static void FreshTable(string? database = null)
    {
        var registry = new ModelMapRegistry();
        registry.Register(new StampRowMapping());
        registry.ApplyToDatabase();

        Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE", database);
        var connector = new PostgreSQLConnector(Settings(database));
        connector.CreateTable(new[] { typeof(StampRow) });
    }

    private static AsyncPostgreSQLStore<StampRow> AsyncStore(string? database = null)
    {
        var store = new AsyncPostgreSQLStore<StampRow>();
        store.SetSettings(Settings(database));
        return store;
    }

    private static StampRow Row(string name, DateTimeKind kind = DateTimeKind.Utc)
        => new()
        {
            Guid = Guid.NewGuid(),
            Name = name,
            Ts = DateTime.SpecifyKind(new DateTime(2026, 3, 15, 10, 30, 0), kind),
        };

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE"); } catch { }
    }

    // ============================================================ the loud half: COPY refused Kind=Utc

    [Fact]
    public async Task Bulk_insert_accepts_a_utc_kinded_entity_and_stores_its_utc_wall_clock()
    {
        if (!RequireServer()) return;
        FreshTable();

        await AsyncStore().CreateAsync(new List<StampRow> { Row("bulk") }, null, CancellationToken.None);

        StoredText("bulk").Should().StartWith("2026-03-15 10:30:00",
            "the binary COPY must store the UTC wall clock the caller supplied; against the unfixed connector "
          + "this threw ArgumentException before reaching the server at all");
    }

    [Fact]
    public async Task Bulk_insert_of_a_utc_kinded_entity_round_trips_through_the_store()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();

        await store.CreateAsync(new List<StampRow> { Row("roundtrip") }, null, CancellationToken.None);
        var read = (await store.ReadAsync(CancellationToken.None)).Single();

        read.Ts.Should().Be(ExpectedWallClock, "the wall clock must survive the round trip unshifted");
        read.Ts.Kind.Should().Be(DateTimeKind.Unspecified,
            "a TIMESTAMP column carries no offset, so Kind cannot round-trip — this is the documented "
          + "contract, and pinning it is what stops a later switch to TIMESTAMPTZ landing silently");
    }

    [Fact]
    public async Task A_bulk_written_row_is_found_by_a_filter_bound_from_a_utc_kinded_value()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();
        await store.CreateAsync(new List<StampRow> { Row("filter") }, null, CancellationToken.None);

        // ct named: the bulk ReadAsync(filter, orderBy, limit, offset, ct) overload hides the single-result
        // one, so a positional CancellationToken would bind to the OrderBy slot.
        var found = (await store.ReadAsync(x => x.Ts == Utc, ct: CancellationToken.None)).ToList();

        found.Should().HaveCount(1,
            "the write path and the filter path must agree about the value — normalising only the COPY writer "
          + "would store 10:30 while binding 11:30 into the WHERE clause on a non-UTC server, matching nothing");
    }

    // ============================================================ the contract, stated per Kind

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task Every_kind_stores_the_same_wall_clock(DateTimeKind kind)
    {
        if (!RequireServer()) return;
        FreshTable();

        await AsyncStore().CreateAsync(new List<StampRow> { Row(kind.ToString(), kind) }, null, CancellationToken.None);

        StoredText(kind.ToString()).Should().StartWith("2026-03-15 10:30:00",
            $"the column stores the wall-clock components as supplied, so Kind={kind} must not change what "
          + "lands in it");
    }

    [Fact]
    public void The_column_is_timestamp_without_time_zone()
    {
        if (!RequireServer()) return;
        FreshTable();

        ColumnType().Should().Be("timestamp without time zone",
            "TASK-256 deliberately did NOT change the column type — option 1 (TIMESTAMPTZ) was measured and "
          + "rejected, so a change here means that decision was reversed without updating the rule");
    }

    // ============================================================ the silent half — needs a non-UTC server

    /// <summary>
    /// The one test that can distinguish the parameterised fix from a no-op.
    ///
    /// <para>
    /// On a UTC server both paths store 10:30 whether or not <c>AddParameter</c> normalises, so reverting that
    /// half fails nothing — and a revert that fails nothing is a missing test. This stands up a <b>database of
    /// its own</b> with <c>TimeZone</c> set to <c>Europe/Bratislava</c>, because
    /// <c>PostgreSqlSettings.GetConnectionString()</c> emits no <c>Timezone</c> key and offers no raw escape
    /// hatch — so <c>SET TimeZone</c> on a test's own connection cannot reach the store's connection. A
    /// dedicated database also keeps the GUC off the shared one, so no concurrently-running suite is affected
    /// and a crashed run leaves nothing behind.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="NpgsqlConnection.ClearAllPools"/> is mandatory, not hygiene.</b> Measured: without it a
    /// pooled connection keeps the old <c>Etc/UTC</c> setting and this test silently measures nothing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Parameterised_and_bulk_agree_on_a_non_utc_server()
    {
        if (!RequireServer()) return;

        var tzDatabase = "birko_tz_task256";
        Exec($"DROP DATABASE IF EXISTS {tzDatabase} WITH (FORCE)", Database);
        Exec($"CREATE DATABASE {tzDatabase}", Database);
        try
        {
            Exec($"ALTER DATABASE {tzDatabase} SET TimeZone TO 'Europe/Bratislava'", Database);
            // Without this the pool hands back a connection still on Etc/UTC and the shift never happens.
            NpgsqlConnection.ClearAllPools();

            using (var check = new NpgsqlConnection(Settings(tzDatabase).GetConnectionString()))
            {
                check.Open();
                using var show = check.CreateCommand();
                show.CommandText = "SHOW TimeZone";
                show.ExecuteScalar()?.ToString().Should().Be("Europe/Bratislava",
                    "if the session is not actually non-UTC this test cannot observe the defect it exists for");
            }

            FreshTable(tzDatabase);
            var store = AsyncStore(tzDatabase);

            // Single row -> parameterised path. Collection -> binary COPY.
            await store.CreateAsync(Row("single"), null, CancellationToken.None);
            await store.CreateAsync(new List<StampRow> { Row("many") }, null, CancellationToken.None);

            var single = StoredText("single", tzDatabase);
            var many = StoredText("many", tzDatabase);
            _output.WriteLine($"session TZ=Europe/Bratislava  single={single}  bulk={many}");

            single.Should().StartWith("2026-03-15 10:30:00",
                "the parameterised path must store the supplied UTC wall clock; unfixed, Npgsql infers "
              + "timestamptz and the server casts it through the session TimeZone, storing 11:30");
            many.Should().Be(single,
                "both write paths must store the same instant for the same value — this is what a COPY-only "
              + "fix cannot deliver");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            try { Exec($"DROP DATABASE IF EXISTS {tzDatabase} WITH (FORCE)", Database); } catch { }
        }
    }

    /// <summary>
    /// The bulk <b>update</b> path binds its values <i>without</i> going through <c>AddParameter</c> — it
    /// pre-creates parameters with <c>DBNull.Value</c>, calls <c>command.Prepare()</c>, then assigns
    /// <c>.Value</c> per row — and it is nevertheless correct. `Prepare()` pins each parameter to the target
    /// column's real type before any value is assigned, so a <c>Kind=Utc</c> value is sent as a
    /// <c>timestamp</c> and never re-inferred as <c>timestamptz</c>. Measured on a non-UTC server: unshifted.
    ///
    /// <para>
    /// This is pinned rather than argued, for two reasons. It is the <b>only</b> thing making the "every write
    /// boundary is normalised" claim true for these six binding sites, and it holds by a *different* mechanism
    /// than the two normalised ones — so if `Prepare()` is ever dropped here, or a seventh binding site is
    /// added without it, the silent shift returns and nothing else would notice. Note the mechanism is
    /// provider-specific: on MSSql `Prepare()` throws on untyped placeholders, which is why the bulk
    /// update/delete paths have never worked there at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Bulk_update_does_not_shift_a_utc_value_on_a_non_utc_server()
    {
        if (!RequireServer()) return;

        var tzDatabase = "birko_tz_task256_upd";
        Exec($"DROP DATABASE IF EXISTS {tzDatabase} WITH (FORCE)", Database);
        Exec($"CREATE DATABASE {tzDatabase}", Database);
        try
        {
            Exec($"ALTER DATABASE {tzDatabase} SET TimeZone TO 'Europe/Bratislava'", Database);
            NpgsqlConnection.ClearAllPools();

            FreshTable(tzDatabase);
            var store = AsyncStore(tzDatabase);

            // Seed with a different instant, then bulk-update to the value under test so the assertion cannot
            // pass on the seed.
            var seed = Row("upd");
            seed.Ts = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await store.CreateAsync(new List<StampRow> { seed }, null, CancellationToken.None);

            var loaded = (await store.ReadAsync(CancellationToken.None)).Single();
            loaded.Ts = Utc;
            await store.UpdateAsync(new List<StampRow> { loaded }, null, CancellationToken.None);

            StoredText("upd", tzDatabase).Should().StartWith("2026-03-15 10:30:00",
                "the bulk update path binds outside AddParameter, and command.Prepare() is what keeps it "
              + "correct — if this shifts to 11:30, either Prepare() was removed or a new binding site was "
              + "added without it, and NormalizeTimestampValue must be wired there too");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            try { Exec($"DROP DATABASE IF EXISTS {tzDatabase} WITH (FORCE)", Database); } catch { }
        }
    }

    // ============================================================ the premise the fix rests on

    /// <summary>
    /// Pins the premise <c>NormalizeTimestampValue</c> depends on — <b>as narrowed by TASK-263</b>.
    ///
    /// <para>
    /// TASK-256 wrote this to assert that a <c>DateTime</c> property has exactly <i>one</i> mapping, so no bound
    /// <c>DateTime</c> could target a <c>timestamptz</c> column and stripping <c>Kind</c> unconditionally was
    /// safe. TASK-263 falsified that by adding <c>[UtcField]</c>, which maps a <c>DateTime</c> property to
    /// <c>DbType.DateTimeOffset</c> and therefore to <c>TIMESTAMPTZ</c>. The assertion below still holds, and
    /// still matters, but it now means something narrower: an <b>unmarked</b> <c>DateTime</c> maps to
    /// <c>DbType.DateTime</c>.
    /// </para>
    /// <para>
    /// What keeps <c>NormalizeTimestampValue</c> correct after TASK-263 is not this mapping but the CLR type of
    /// the bound value: <c>UtcDateTimeField.Write</c> returns a <c>DateTimeOffset</c>, which the helper's
    /// <c>is DateTime</c> test does not match. The two-way rule is asserted in
    /// <c>Birko.Data.SQL.Tests.DataBase.UtcFieldMappingTests</c>, and the instant that would be wrong if this
    /// composition broke is asserted in <c>UtcFieldInstantLiveTests</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void A_datetime_property_maps_only_to_DbType_DateTime()
    {
        var field = Birko.Data.SQL.Fields.AbstractField.CreateAbstractField(
            typeof(StampRow).GetProperty(nameof(StampRow.Ts))!);

        field.Should().NotBeNull();
        field!.Type.Should().Be(DbType.DateTime,
            "an UNMARKED DateTime property is a wall clock. A [UtcField] one reaches DbType.DateTimeOffset "
          + "(TASK-263) and is safe from the Kind-stripper only because its Write binds a DateTimeOffset — so "
          + "if this ever changes for an unmarked property, that stripper is discarding a real offset");
    }
}
