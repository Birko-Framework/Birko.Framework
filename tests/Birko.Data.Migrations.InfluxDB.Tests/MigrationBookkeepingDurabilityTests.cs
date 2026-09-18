using System;
using System.IO;
using Birko.Data.Migrations.InfluxDB;
using FluentAssertions;
using InfluxDB.Client;
using Xunit;

namespace Birko.Data.Migrations.InfluxDB.Tests;

/// <summary>
/// <b>SH-H030 and SH-H033 (TASK-314).</b> Two independent ways this store under-reported which migrations
/// had been applied. Both end in the same place: <c>GetCurrentVersion()</c> yields 0 and the next
/// <c>Migrate()</c> replays every registered migration — including any destructive <c>Up</c> — against a
/// live, already-migrated database.
///
/// <list type="bullet">
/// <item><b>SH-H030</b>: <c>GetAppliedVersions</c> swallowed every <c>InfluxException</c>. The existing
/// CR-L146 comment already conceded it could not separate "the bucket has no data yet" from an invalid
/// token, a wrong organization or a connectivity failure — and swallowing is the wrong side of that
/// ambiguity. A spurious throw costs one failed run that says why; a spurious empty set costs the
/// database.</item>
/// <item><b>SH-H033</b>: the <c>_migrations</c> bucket was created with a 365-day <c>Expire</c> rule, and
/// each point is timestamped with <c>migration.CreatedAt</c> — the migration's <i>authored</i> date, not
/// when it ran. So this is worse than "records expire after a year": a migration authored more than a
/// year ago falls outside the retention window the moment it is written.</item>
/// </list>
/// </summary>
public class MigrationBookkeepingDurabilityTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public MigrationBookkeepingDurabilityTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    // ---------------------------------------------------------------- SH-H030

    /// <summary>
    /// <b>The swallow cannot be reached offline, and establishing that took two wrong attempts — both
    /// recorded here so the next reader does not repeat them.</b>
    ///
    /// <list type="number">
    /// <item>Pointing the client at a closed port does not reach the catch at all:
    /// <c>GetAppliedVersions</c> opens with <c>EnsureInitialized()</c> → <c>Initialize()</c> →
    /// <c>FindBucketsAsync()</c>, which is <b>outside</b> the try. Measured: an
    /// <c>InfluxDB.Client.Core.Exceptions.HttpException</c> from bucket discovery. § TASK-291 records this
    /// exact trap — <i>before provoking a condition, check which side of the try it lands on</i>.</item>
    /// <item>Pre-seeding <c>_migrationsBucket</c> does skip initialisation and does reach
    /// <c>QueryAsync</c> inside the try — but a connection refusal there surfaces as a raw
    /// <c>System.Net.Http.HttpRequestException</c>, which is <b>not</b> an <c>InfluxException</c> and so
    /// was never caught by the old swallow either. Measured.</item>
    /// </list>
    ///
    /// <para>
    /// So the swallow only ever fired for failures InfluxDB itself <i>reports</i> — a rejected token, a
    /// wrong organization, a malformed query — and producing one needs a live server. The offline cover is
    /// therefore the source scan below, which is weaker than a behavioural assertion and is the honest
    /// alternative to a test that passes either way. <see cref="An_authentication_failure_is_not_reported_as_an_empty_applied_set"/>
    /// is the behavioural half, gated on a live server.
    /// </para>
    /// </summary>
    [Fact]
    public void The_failed_read_is_rethrown_rather_than_swallowed()
    {
        var source = ReadStoreSource();

        source.Should().Contain("Cannot read applied migration versions from bucket",
            "a read that could not be answered must not come back as an empty applied set — SH-H030");
        source.Should().Contain("replay every registered migration",
            "the refusal has to say what an empty set would have caused, or a reader reaches around it");
    }

    /// <summary>
    /// <b>Contract pin, not evidence.</b> A transport-level refusal has always propagated — it is not an
    /// <c>InfluxException</c> — so this passes with the swallow restored too. It is here to pin that the
    /// fix did not accidentally start swallowing the one failure that already escaped.
    /// </summary>
    [Fact]
    public void A_transport_failure_still_propagates()
    {
        var store = new InfluxMigrationStore(new InfluxDBClient("http://localhost:59996", "token"), "org");

        Action act = () => store.GetAppliedVersions();

        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// <b>The behavioural half.</b> A rejected token is a failure InfluxDB reports as an
    /// <c>InfluxException</c>, which is precisely what the old catch swallowed — so this is the one shape
    /// that distinguishes the fix, and it needs a live server. Gated on <c>BIRKO_INFLUX_HOST</c>; set
    /// <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure rather than a skip.
    /// </summary>
    [Fact]
    public void An_authentication_failure_is_not_reported_as_an_empty_applied_set()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_INFLUX_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            const string message = "SKIPPED: no live InfluxDB. Set BIRKO_INFLUX_HOST to exercise this test; "
                                 + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            {
                throw new InvalidOperationException(message);
            }
            return;
        }

        var port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_INFLUX_PORT"), out var p) ? p : 8086;
        var store = new InfluxMigrationStore(
            new InfluxDBClient($"http://{host}:{port}", "a-token-the-server-will-reject"), "org");

        var returnedNormally = true;
        try
        {
            store.GetAppliedVersions();
        }
        catch
        {
            returnedNormally = false;
        }

        returnedNormally.Should().BeFalse(
            "a rejected token used to be swallowed as an empty applied set, which makes GetCurrentVersion() "
          + "0 and replays every registered migration against a live, already-migrated database");
    }

    // ---------------------------------------------------------------- SH-H033

    /// <summary>
    /// Bucket creation needs a live InfluxDB, so the durability property is pinned on the source: the
    /// retention rule this store creates the bookkeeping bucket with must be <c>0</c>, InfluxDB's spelling
    /// for infinite. A source scan is weaker than a behavioural assertion and is the honest alternative to
    /// no cover at all — it fails if anyone reinstates a finite expiry.
    /// </summary>
    [Fact]
    public void The_bookkeeping_bucket_is_created_with_infinite_retention()
    {
        var source = ReadStoreSource();

        source.Should().NotContain("365L * 86400L",
            "migration bookkeeping must never expire — see SH-H033");
        source.Should().Contain("BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, 0L)");
    }

    /// <summary>
    /// The timestamping is what makes the expiry worse than it looks, and it is deliberately unchanged:
    /// with infinite retention a <c>CreatedAt</c>-stamped point is durable, and re-applying a migration
    /// overwrites its own row rather than adding a second. This pins the pairing, so a later change that
    /// reinstates an expiry has to confront it.
    /// </summary>
    [Fact]
    public void Records_are_still_stamped_with_the_migrations_authored_date()
    {
        ReadStoreSource().Should().Contain(".Timestamp(migration.CreatedAt, WritePrecision.Ms)");
    }

    private static string ReadStoreSource()
    {
        // Walk up looking for the project directory, trying both the level itself and a
        // "Framework" child at that level. A FIXED depth breaks whenever the layout changes: the
        // monorepo migration added a "tests/" segment, so the old `for i<6` landed one level too
        // deep and produced .../Framework/Framework/... (AppContext.BaseDirectory has a trailing
        // separator, so the first GetDirectoryName only strips it). This shape survives both.
        for (var probe = new DirectoryInfo(AppContext.BaseDirectory); probe != null; probe = probe.Parent)
        {
            foreach (var root in new[] { probe.FullName, Path.Combine(probe.FullName, "Framework") })
            {
                var candidate = Path.Combine(root, "Birko.Data.Migrations.InfluxDB", "InfluxMigrationStore.cs");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
            }
        }
        throw new FileNotFoundException("the source scan must actually find InfluxMigrationStore.cs");
    }
}
