using System;
using System.Linq;
using Birko.Data.Migrations.InfluxDB.Context;
using FluentAssertions;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.InfluxDB.Tests;

/// <summary>
/// TASK-722, live. Measured before the fix: <c>DeleteDocuments</c> with no filter — "delete everything" — deleted
/// <c>UtcNow − 100 years</c> to <c>UtcNow</c>, so a point stamped in the future (a schedule, a forecast) or older than a
/// century survived it.
/// </summary>
public sealed class Task722DeleteRangeLiveTests : IDisposable
{
    private const string Org = "birko";
    private readonly ITestOutputHelper _output;
    private readonly InfluxDBClient? _client;
    private readonly string _bucket = "t722_" + Guid.NewGuid().ToString("N").Substring(0, 12);

    public Task722DeleteRangeLiveTests(ITestOutputHelper output)
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
        foreach (var bucket in buckets.FindBucketsAsync(limit: 100).GetAwaiter().GetResult().Where(b => b.Name == _bucket))
        {
            try { buckets.DeleteBucketAsync(bucket).GetAwaiter().GetResult(); } catch { }
        }
        _client.Dispose();
    }

    [Fact]
    public void Deleting_everything_deletes_future_and_old_points_too()
    {
        if (_client == null) return;
        var orgId = _client.GetOrganizationsApi().FindOrganizationsAsync(org: Org).GetAwaiter().GetResult().Single().Id;
        _client.GetBucketsApi().CreateBucketAsync(_bucket, (BucketRetentionRules?)null, orgId).GetAwaiter().GetResult();
        var write = _client.GetWriteApiAsync();
        foreach (var at in new[] { DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(30), new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc) })
        {
            write.WritePointAsync(PointData.Measurement("m").Field("v", 1).Timestamp(at, WritePrecision.Ns), _bucket, Org).GetAwaiter().GetResult();
        }

        new InfluxDBDataMigrator(_client, Org).DeleteDocuments(_bucket, null!);

        var left = _client.GetQueryApi().QueryAsync(
            $"from(bucket: \"{_bucket}\") |> range(start: 1677-09-22T00:00:00Z, stop: 2262-04-11T00:00:00Z) |> count()", Org)
            .GetAwaiter().GetResult().SelectMany(t => t.Records).Sum(r => Convert.ToInt64(r.GetValue()));
        left.Should().Be(0);
    }
}
