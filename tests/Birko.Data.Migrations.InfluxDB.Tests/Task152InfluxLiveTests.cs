using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.InfluxDB.Context;
using FluentAssertions;
using InfluxDB.Client;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.InfluxDB.Tests;

/// <summary>
/// TASK-152, live InfluxDB 2: the bookkeeping store removes a version recorded in an earlier process (SH-M188), keeps two
/// versions sharing a name (SH-M189) and reads a migration created long ago; the data migrator refuses what it cannot do
/// instead of reporting success (SH-M191, M192, M193, M195, M196) and copies a series as itself (SH-M194).
/// Gated on <c>BIRKO_INFLUX_HOST</c>.
/// </summary>
public sealed class Task152InfluxLiveTests : IDisposable
{
    private const string Org = "birko";
    private readonly ITestOutputHelper _output;
    private readonly InfluxDBClient? _client;
    private readonly string _prefix = "t152_" + Guid.NewGuid().ToString("N").Substring(0, 12);

    public Task152InfluxLiveTests(ITestOutputHelper output)
    {
        _output = output;
        var host = Environment.GetEnvironmentVariable("BIRKO_INFLUX_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            const string message = "SKIPPED: no live InfluxDB. Set BIRKO_INFLUX_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return;
        }
        var port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_INFLUX_PORT"), out var p) ? p : 8086;
        var user = Environment.GetEnvironmentVariable("BIRKO_INFLUX_USER") ?? "birko";
        var password = Environment.GetEnvironmentVariable("BIRKO_INFLUX_PASSWORD") ?? "birkopassword";
        _client = new InfluxDBClient(new InfluxDBClientOptions($"http://{host}:{port}") { Username = user, Password = password });
    }

    public void Dispose()
    {
        if (_client == null) return;
        var buckets = _client.GetBucketsApi();
        foreach (var bucket in buckets.FindBucketsAsync(limit: 100).GetAwaiter().GetResult().Where(b => b.Name.StartsWith(_prefix)))
        {
            try { buckets.DeleteBucketAsync(bucket).GetAwaiter().GetResult(); } catch { }
        }
        _client.Dispose();
    }

    private sealed class Mig : IMigration
    {
        public Mig(long version, string name, DateTime createdAt) { Version = version; Name = name; CreatedAt = createdAt; }
        public long Version { get; }
        public string Name { get; }
        public string Description => Name;
        public DateTime CreatedAt { get; }
        public void Up(IMigrationContext context) { }
        public void Down(IMigrationContext context) { }
    }

    private InfluxMigrationStore Store()
    {
        var store = new InfluxMigrationStore(_client!, Org, _prefix + "_migrations");
        store.Initialize();
        return store;
    }

    [Fact]
    public void A_version_recorded_in_an_earlier_process_is_removed()
    {
        if (_client == null) return;
        var store = Store();
        store.RecordMigration(new Mig(5, "AddOrders", DateTime.UtcNow.AddDays(-3)));

        // A later process constructs the migration again: its CreatedAt is "now", days after the recorded point.
        store.RemoveMigration(new Mig(5, "AddOrders", DateTime.UtcNow));

        store.GetAppliedVersions().Should().BeEmpty();
    }

    [Fact]
    public void Two_versions_sharing_a_name_and_a_millisecond_are_both_kept_and_removed_apart()
    {
        if (_client == null) return;
        var store = Store();
        var at = DateTime.UtcNow.AddHours(-1);
        store.RecordMigration(new Mig(1, "Seed", at));
        store.RecordMigration(new Mig(2, "Seed", at));

        store.GetAppliedVersions().Should().BeEquivalentTo(new[] { 1L, 2L });

        store.RemoveMigration(new Mig(1, "Seed", DateTime.UtcNow));
        store.GetAppliedVersions().Should().BeEquivalentTo(new[] { 2L });
    }

    [Fact]
    public void A_migration_created_long_ago_is_read_back()
    {
        if (_client == null) return;
        var store = Store();
        store.RecordMigration(new Mig(7, "Legacy", new DateTime(2012, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        store.GetAppliedVersions().Should().Contain(7);
    }

    [Fact]
    public void The_data_migrator_refuses_what_it_cannot_do()
    {
        if (_client == null) return;
        var data = new InfluxDBDataMigrator(_client, Org);
        new InfluxDBSchemaBuilder(_client, Org).CreateCollection(_prefix + "_rows").Build();

        data.Invoking(d => d.UpdateDocuments(_prefix + "_rows", "{}", new Dictionary<string, object> { ["x"] = 1 }))
            .Should().Throw<NotSupportedException>();
        data.Invoking(d => d.CountDocuments(_prefix + "_rows", "{\"host\":\"a\"}")).Should().Throw<NotSupportedException>();
        data.Invoking(d => d.DeleteDocuments(_prefix + "_no_such_bucket", "")).Should().Throw<InvalidOperationException>();
        data.Invoking(d => d.BulkInsert(_prefix + "_rows", new[] { new Dictionary<string, object> { ["host"] = "a" } }))
            .Should().Throw<NotSupportedException>();
        data.Invoking(d => d.CopyData(_prefix + "_rows", _prefix + "_copy", "{\"x\":1}")).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void A_copy_reproduces_the_series_fields_and_tags()
    {
        if (_client == null) return;
        var schema = new InfluxDBSchemaBuilder(_client, Org);
        schema.CreateCollection(_prefix + "_src").Build();
        schema.CreateCollection(_prefix + "_dst").Build();
        var data = new InfluxDBDataMigrator(_client, Org);
        data.BulkInsert(_prefix + "_src", new[]
        {
            new Dictionary<string, object> { ["_measurement"] = "cpu", ["host"] = "a", ["usage"] = 0.5, ["time"] = DateTime.UtcNow.AddMinutes(-2) },
            new Dictionary<string, object> { ["_measurement"] = "cpu", ["host"] = "b", ["usage"] = 0.7, ["time"] = DateTime.UtcNow.AddMinutes(-1) },
        });

        data.CopyData(_prefix + "_src", _prefix + "_dst");

        var tables = _client.GetQueryApi().QueryAsync($"from(bucket: \"{_prefix}_dst\") |> range(start: 0)", Org).GetAwaiter().GetResult();
        var records = tables.SelectMany(t => t.Records).ToList();
        foreach (var r in records) _output.WriteLine(string.Join(", ", r.Values.Select(kv => kv.Key + "=" + kv.Value)));
        records.Should().HaveCount(2);
        records.Should().OnlyContain(r => r.GetField() == "usage" && r.GetMeasurement() == "cpu");
        records.Select(r => r.GetValueByKey("host")).Should().BeEquivalentTo(new object[] { "a", "b" });
        records.Should().OnlyContain(r => !r.Values.ContainsKey("_original_bucket") && !r.Values.ContainsKey("result_"));
        data.CountDocuments(_prefix + "_dst").Should().Be(2);
    }
}
