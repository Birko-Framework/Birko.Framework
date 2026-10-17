using System;
using System.Linq;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.InfluxDB.Context;
using Birko.Data.Patterns.Schema;
using FluentAssertions;
using InfluxDB.Client;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.InfluxDB.Tests;

/// <summary>
/// TASK-642: InfluxDB can neither rename nor drop a field, so a migration that asks for either must fail and stay
/// unrecorded; both were silent no-ops, and the runner recorded the migration as applied. Live, gated on
/// <c>BIRKO_INFLUX_HOST</c>; the refusals themselves are checked without a server.
/// </summary>
public sealed class Task642InfluxFieldOpsLiveTests : IDisposable
{
    private const string Org = "birko";
    private readonly ITestOutputHelper _output;
    private readonly InfluxDBClient? _client;
    private readonly string _prefix = "t642_" + Guid.NewGuid().ToString("N").Substring(0, 12);

    public Task642InfluxFieldOpsLiveTests(ITestOutputHelper output)
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

    private sealed class FieldMigration : AbstractMigration
    {
        private readonly Action<ISchemaBuilder> _up;
        public FieldMigration(Action<ISchemaBuilder> up) => _up = up;
        public override long Version => 1;
        public override string Name => "FieldOp";
        public override void Up(IMigrationContext context) => _up(context.Schema);
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("drop")]
    public void A_migration_that_renames_or_drops_a_field_fails_and_is_not_recorded(string op)
    {
        if (_client == null) return;
        var runner = new InfluxMigrationRunner(_client, Org, _prefix + "_migrations");
        runner.Initialize();
        runner.RegisterMigrations(new FieldMigration(schema =>
        {
            if (op == "rename") schema.RenameField(_prefix + "_rows", "temp", "temperature");
            else schema.DropField(_prefix + "_rows", "temp");
        }));

        MigrationResult? result = null;
        Exception? thrown = null;
        try { result = runner.Migrate(); } catch (Exception ex) { thrown = ex; }
        _output.WriteLine($"result: success={result?.Success} thrown={thrown?.GetType().Name}: {thrown?.Message}");

        (thrown != null || result?.Success == false).Should().BeTrue("a field operation InfluxDB cannot perform must not succeed");
        runner.CurrentVersion.Should().Be(0, "the migration did nothing, so it must not be recorded as applied");
    }

    [Fact]
    public void RenameField_and_DropField_refuse_and_say_why()
    {
        var schema = new InfluxDBSchemaBuilder(new InfluxDBClient("http://localhost:1"), Org);

        schema.Invoking(s => s.RenameField("rows", "temp", "temperature")).Should().Throw<NotSupportedException>().WithMessage("*rename*");
        schema.Invoking(s => s.DropField("rows", "temp")).Should().Throw<NotSupportedException>().WithMessage("*drop*");
    }
}
