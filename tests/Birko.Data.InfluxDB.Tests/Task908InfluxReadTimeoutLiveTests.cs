using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InfluxDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.InfluxDB.Tests;

public class T908Point : AbstractModel
{
    public string? Kind { get; set; }
    public int? Score { get; set; }
}

/// <summary>
/// TASK-908, live InfluxDB: <see cref="Settings.RequestTimeout"/> decides whether a large read and aggregate finish. Every
/// point is its own series (the <c>Guid</c> tag), so reading 100k points takes 45–50 s and fails at the client's 10 s
/// default (measured 2026-11-11: <c>TaskCanceledException</c> after 10.05 s for the read, 10.02 s for the aggregate).
/// Gated on <c>BIRKO_INFLUX_HOST</c> like <see cref="Task906InfluxAggregateLiveTests"/>; the 100k-point case also needs
/// <c>BIRKO_INFLUX_HEAVY</c>, since it runs for over a minute.
/// </summary>
public sealed class Task908InfluxReadTimeoutLiveTests
{
    private const string Org = "birko";
    private readonly ITestOutputHelper _output;
    public Task908InfluxReadTimeoutLiveTests(ITestOutputHelper output) => _output = output;

    private static readonly AggregateQuery<T908Point> CountAndSumByKind = new()
    {
        GroupByFields = new[] { "Kind" },
        Aggregates = new AggregateField[] { new(AggregateFunction.Count, "Kind", "n"), new(AggregateFunction.Sum, "Score", "s") },
    };

    [Fact]
    public async Task A_short_timeout_fails_a_10k_point_read_and_a_long_one_completes_it()
    {
        await WithSeededBucket(10_000, async (url, bucket, token) =>
        {
            using var shortStore = Store(url, bucket, token, TimeSpan.FromMilliseconds(300));
            var watch = Stopwatch.StartNew();
            var shortRead = async () => await shortStore.ReadAsync(CancellationToken.None);
            await shortRead.Should().ThrowAsync<OperationCanceledException>();
            _output.WriteLine($"300 ms timeout: failed after {watch.ElapsedMilliseconds} ms");
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

            using var longStore = Store(url, bucket, token, TimeSpan.FromMinutes(2));
            watch.Restart();
            (await longStore.ReadAsync(CancellationToken.None)).Should().HaveCount(10_000);
            _output.WriteLine($"2 min timeout: read 10k in {watch.ElapsedMilliseconds} ms");
        });
    }

    [Fact]
    public async Task Over_100k_points_the_default_fails_and_a_long_timeout_answers()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_INFLUX_HEAVY")))
        {
            _output.WriteLine("SKIPPED: set BIRKO_INFLUX_HEAVY to run the 100k-point case (over a minute).");
            return;
        }
        await WithSeededBucket(100_000, async (url, bucket, token) =>
        {
            using var defaultStore = Store(url, bucket, token, null);
            var watch = Stopwatch.StartNew();
            var defaultRead = async () => await defaultStore.ReadAsync(CancellationToken.None);
            await defaultRead.Should().ThrowAsync<OperationCanceledException>();
            _output.WriteLine($"default timeout: read failed after {watch.ElapsedMilliseconds} ms");
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));

            using var longStore = Store(url, bucket, token, TimeSpan.FromMinutes(3));
            watch.Restart();
            (await longStore.ReadAsync(CancellationToken.None)).Should().HaveCount(100_000);
            _output.WriteLine($"3 min timeout: read 100k in {watch.ElapsedMilliseconds} ms");
            watch.Restart();
            var groups = await longStore.AggregateAsync(CountAndSumByKind);
            _output.WriteLine($"3 min timeout: aggregate in {watch.ElapsedMilliseconds} ms");
            groups.Should().HaveCount(10);
            groups.Sum(g => Convert.ToInt64(g.Values["n"])).Should().Be(100_000);
        });
    }

    private static AsyncInfluxDBStore<T908Point> Store(string url, string bucket, string token, TimeSpan? timeout)
    {
        var store = new AsyncInfluxDBStore<T908Point>();
        store.SetSettings(new Settings(url, bucket, token, Org) { RequestTimeout = timeout });
        return store;
    }

    private async Task WithSeededBucket(int points, Func<string, string, string, Task> body)
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_INFLUX_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            const string message = "SKIPPED: no live InfluxDB. Set BIRKO_INFLUX_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }
        var port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_INFLUX_PORT"), out var p) ? p : 8086;
        var url = $"http://{host}:{port}";
        var user = Environment.GetEnvironmentVariable("BIRKO_INFLUX_USER") ?? "birko";
        var password = Environment.GetEnvironmentVariable("BIRKO_INFLUX_PASSWORD") ?? "birkopassword";

        using var admin = new global::InfluxDB.Client.InfluxDBClient(new InfluxDBClientOptions(url) { Username = user, Password = password });
        var org = (await admin.GetOrganizationsApi().FindOrganizationsAsync(org: Org)).First();
        var bucket = await admin.GetBucketsApi().CreateBucketAsync("birko_task908_" + Guid.NewGuid().ToString("N"), org.Id);
        var resource = new PermissionResource(type: PermissionResource.TypeBuckets, id: bucket.Id, orgID: org.Id);
        var token = await admin.GetAuthorizationsApi().CreateAuthorizationAsync(org.Id, new List<Permission>
        {
            new(Permission.ActionEnum.Read, resource),
            new(Permission.ActionEnum.Write, resource),
        });
        try
        {
            using (var writer = Store(url, bucket.Name, token.Token, TimeSpan.FromMinutes(1)))
            {
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < points; i += 5000)
                {
                    var batch = Enumerable.Range(i, Math.Min(5000, points - i)).Select(k => new T908Point { Kind = "k" + (k % 10), Score = k % 100 }).ToList();
                    await writer.CreateAsync(batch);
                }
                _output.WriteLine($"seeded {points} points in {watch.ElapsedMilliseconds} ms");
            }
            await body(url, bucket.Name, token.Token);
        }
        finally
        {
            try { await admin.GetAuthorizationsApi().DeleteAuthorizationAsync(token); } catch { }
            try { await admin.GetBucketsApi().DeleteBucketAsync(bucket); } catch { }
        }
    }
}
