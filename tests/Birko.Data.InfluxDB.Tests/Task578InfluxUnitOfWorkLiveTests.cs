using System;
using System.Threading.Tasks;
using Birko.Data.InfluxDB.UnitOfWork;
using Birko.Data.Patterns.UnitOfWork;
using FluentAssertions;
using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.InfluxDB.Tests;

/// <summary>
/// TASK-578 (SH-M431), live: a failed InfluxDB write ends the unit of work instead of leaving the same points queued,
/// so a retry does not silently re-send what may already be written, as on Elasticsearch (TASK-325). Gated on
/// <c>BIRKO_INFLUX_HOST</c>.
/// </summary>
public sealed class Task578InfluxUnitOfWorkLiveTests : IDisposable
{
    private const string Org = "birko";
    private readonly ITestOutputHelper _output;
    private readonly global::InfluxDB.Client.InfluxDBClient? _client;

    public Task578InfluxUnitOfWorkLiveTests(ITestOutputHelper output)
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
        _client = new global::InfluxDB.Client.InfluxDBClient(new InfluxDBClientOptions($"http://{host}:{port}") { Username = user, Password = password });
    }

    public void Dispose() => _client?.Dispose();

    [Fact]
    public async Task A_failed_write_ends_the_boundary_and_the_following_rollback_is_a_no_op()
    {
        if (_client == null) return;
        using var uow = new InfluxDbUnitOfWork(_client, "birko_task578_missing_" + Guid.NewGuid().ToString("N"), Org);
        await uow.BeginAsync();
        uow.Context!.AddPoint(PointData.Measurement("t578").Field("v", 1));

        var commit = () => uow.CommitAsync();

        await commit.Should().ThrowAsync<Exception>("a write to a bucket that does not exist is refused");
        uow.IsActive.Should().BeFalse("the points are spent once sent; leaving them queued made a retry re-send all of them");
        uow.Context.Should().BeNull();
        await uow.Invoking(u => u.RollbackAsync()).Should().NotThrowAsync("catch-and-roll-back after a failed commit");
        await uow.Invoking(u => u.RollbackAsync()).Should().ThrowAsync<NoActiveTransactionException>();
    }
}
