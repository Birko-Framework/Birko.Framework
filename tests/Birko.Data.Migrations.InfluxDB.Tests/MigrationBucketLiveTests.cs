using System;
using System.Linq;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.InfluxDB;
using FluentAssertions;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.InfluxDB.Tests;

/// <summary>
/// TASK-617 — the migration store's bookkeeping bucket was <c>_migrations</c>, which InfluxDB 2 refuses (leading
/// underscore is reserved for system buckets), so no migration could be recorded on a fresh server.
/// </summary>
public class MigrationBucketNameTests
{
    private static InfluxDBClient Client() => new("http://localhost:1", "token");

    [Fact]
    public void The_default_bucket_is_one_InfluxDB_accepts()
    {
        InfluxMigrationStore.DefaultMigrationsBucketName.Should().NotStartWith("_");
        new InfluxMigrationStore(Client(), "org").BucketName.Should().Be(InfluxMigrationStore.DefaultMigrationsBucketName);
    }

    [Fact]
    public void A_caller_named_bucket_is_used()
        => new InfluxMigrationStore(Client(), "org", "app_migrations").BucketName.Should().Be("app_migrations");

    [Theory]
    [InlineData("_migrations")]
    [InlineData(" ")]
    public void A_bucket_name_InfluxDB_refuses_is_refused_up_front(string name)
        => ((Action)(() => new InfluxMigrationStore(Client(), "org", name))).Should().Throw<ArgumentException>().WithMessage("*'_'*");
}

/// <summary>
/// TASK-617, live: on an organization that has never run a migration, the store creates its bucket with infinite
/// retention (SH-H033), records and reads back a migration, and a second store adopts the same bucket.
/// <para>Gated on <c>BIRKO_INFLUX_HOST</c>; logs in as <c>BIRKO_INFLUX_USER</c>/<c>BIRKO_INFLUX_PASSWORD</c>
/// (default <c>birko</c>/<c>birkopassword</c>, as live-tests.yml sets up).</para>
/// </summary>
public sealed class MigrationBucketLiveTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _org = "fresh" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private InfluxDBClient? _client;

    public MigrationBucketLiveTests(ITestOutputHelper output) => _output = output;

    private sealed class SampleMigration : Data.Migrations.AbstractMigration
    {
        public override long Version => 20261014;
        public override string Name => "Sample";
        public override void Up(IMigrationContext context) { }
    }

    private InfluxDBClient? Client()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_INFLUX_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            const string message = "SKIPPED: no live InfluxDB. Set BIRKO_INFLUX_HOST to exercise this test; "
                                 + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return null;
        }
        var port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_INFLUX_PORT"), out var p) ? p : 8086;
        var user = Environment.GetEnvironmentVariable("BIRKO_INFLUX_USER") ?? "birko";
        var password = Environment.GetEnvironmentVariable("BIRKO_INFLUX_PASSWORD") ?? "birkopassword";
        _client = new InfluxDBClient(new InfluxDBClientOptions($"http://{host}:{port}") { Username = user, Password = password });
        _client.GetOrganizationsApi().CreateOrganizationAsync(_org).GetAwaiter().GetResult();
        return _client;
    }

    public void Dispose()
    {
        if (_client == null) return;
        try
        {
            var orgs = _client.GetOrganizationsApi();
            orgs.DeleteOrganizationAsync(orgs.FindOrganizationsAsync(org: _org).GetAwaiter().GetResult().Single()).GetAwaiter().GetResult();
        }
        catch { /* best-effort */ }
        _client.Dispose();
    }

    [Fact]
    public void A_fresh_organization_gets_a_durable_bucket_and_records_round_trip()
    {
        if (Client() == null) return;

        var store = new InfluxMigrationStore(_client!, _org);
        store.Initialize();

        var bucket = _client!.GetBucketsApi().FindBucketsAsync(org: _org, name: InfluxMigrationStore.DefaultMigrationsBucketName)
            .GetAwaiter().GetResult().Single();
        bucket.RetentionRules.Should().OnlyContain(r => r.EverySeconds == 0, "bookkeeping must never expire (SH-H033)");

        var migration = new SampleMigration();
        store.RecordMigration(migration);
        store.GetAppliedVersions().Should().Contain(20261014);

        var second = new InfluxMigrationStore(_client, _org);
        second.Initialize();
        second.GetAppliedVersions().Should().Contain(20261014, "a second store adopts the bucket, never a fresh one");
        _client.GetBucketsApi().FindBucketsAsync(orgID: bucket.OrgID).GetAwaiter().GetResult()
            .Count(b => b.Name == InfluxMigrationStore.DefaultMigrationsBucketName).Should().Be(1);

    }
}
