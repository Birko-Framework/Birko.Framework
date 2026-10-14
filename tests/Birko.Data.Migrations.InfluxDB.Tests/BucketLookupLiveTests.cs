using System;
using System.Linq;
using Birko.Data.Migrations.InfluxDB;
using Birko.Data.Migrations.InfluxDB.Context;
using FluentAssertions;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.InfluxDB.Tests;

/// <summary>
/// TASK-612, live. InfluxDB 2 bucket names are case-sensitive (measured 2026-10-14 on influxdb:2: <c>metrics</c> and
/// <c>Metrics</c> coexist, and <c>GET /api/v2/buckets?name=Metrics</c> returns only <c>Metrics</c>). The migration
/// lookups matched names case-insensitively, read only the first page (20 buckets) and ignored the organization,
/// so <c>DropCollection("Metrics")</c> deleted <c>metrics</c>.
/// <para>Gated on <c>BIRKO_INFLUX_HOST</c>; logs in as <c>BIRKO_INFLUX_USER</c>/<c>BIRKO_INFLUX_PASSWORD</c>
/// (default <c>birko</c>/<c>birkopassword</c>, org <c>birko</c>, as live-tests.yml sets up).</para>
/// </summary>
public sealed class BucketLookupLiveTests : IDisposable
{
    private const string Org = "birko";
    private readonly ITestOutputHelper _output;
    private readonly string _suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
    private InfluxDBClient? _client;

    public BucketLookupLiveTests(ITestOutputHelper output) => _output = output;

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
        return _client = new InfluxDBClient(new InfluxDBClientOptions($"http://{host}:{port}") { Username = user, Password = password });
    }

    private string Name(string stem) => stem + _suffix;

    private bool Exists(string name, string org = Org)
    {
        try
        {
            return _client!.GetBucketsApi().FindBucketsAsync(org: org, name: name).GetAwaiter().GetResult().Any(b => b.Name == name);
        }
        catch (global::InfluxDB.Client.Core.Exceptions.NotFoundException)
        {
            return false; // the server answers a name filter that matches nothing with 404
        }
    }

    private void Create(string name, string org = Org)
    {
        var orgId = _client!.GetOrganizationsApi().FindOrganizationsAsync(org: org).GetAwaiter().GetResult().Single().Id;
        _client.GetBucketsApi().CreateBucketAsync(name, (BucketRetentionRules?)null, orgId).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (_client == null) return;
        var api = _client.GetBucketsApi();
        foreach (var b in api.FindBucketsAsync(limit: 100).GetAwaiter().GetResult().Where(b => b.Name.EndsWith(_suffix, StringComparison.OrdinalIgnoreCase)))
        {
            try { api.DeleteBucketAsync(b).GetAwaiter().GetResult(); } catch { }
        }
        _client.Dispose();
    }

    [Fact]
    public void Dropping_a_bucket_never_deletes_one_that_differs_only_in_case()
    {
        if (Client() == null) return;
        Create(Name("metrics"));

        new InfluxDBSchemaBuilder(_client!, Org).DropCollection(Name("Metrics"));

        Exists(Name("metrics")).Should().BeTrue("Metrics and metrics are different buckets");
    }

    [Fact]
    public void A_bucket_differing_only_in_case_neither_exists_nor_blocks_creating_the_declared_one()
    {
        if (Client() == null) return;
        Create(Name("metrics"));
        var builder = new InfluxDBSchemaBuilder(_client!, Org);

        builder.CollectionExists(Name("Metrics")).Should().BeFalse();
        builder.CreateCollection(Name("Metrics"));

        Exists(Name("Metrics")).Should().BeTrue();
        Exists(Name("metrics")).Should().BeTrue();
    }

    /// <summary>
    /// The server answers an unlimited bucket listing with one page of 20 (measured: 20 of 32). Which buckets fall on
    /// it depends on the listing order, so every bucket is checked: with more than 20, some are always off the page.
    /// </summary>
    [Fact]
    public void Every_bucket_is_found_however_many_there_are()
    {
        if (Client() == null) return;
        var names = Enumerable.Range(0, 22).Select(i => Name($"page{i:00}x")).ToList();
        names.ForEach(n => Create(n));
        var builder = new InfluxDBSchemaBuilder(_client!, Org);

        names.Where(n => !builder.CollectionExists(n)).Should().BeEmpty();
    }

    [Fact]
    public void A_bucket_of_the_same_name_in_another_organization_is_not_ours()
    {
        if (Client() == null) return;
        var other = "other" + _suffix;
        _client!.GetOrganizationsApi().CreateOrganizationAsync(other).GetAwaiter().GetResult();
        try
        {
            Create(Name("shared"), other);
            var builder = new InfluxDBSchemaBuilder(_client, Org);

            builder.CollectionExists(Name("shared")).Should().BeFalse();
            builder.DropCollection(Name("shared"));
            Exists(Name("shared"), other).Should().BeTrue("another organization's bucket is not this migration's to drop");
        }
        finally
        {
            var orgs = _client.GetOrganizationsApi();
            orgs.DeleteOrganizationAsync(orgs.FindOrganizationsAsync(org: other).GetAwaiter().GetResult().Single()).GetAwaiter().GetResult();
        }
    }
}
