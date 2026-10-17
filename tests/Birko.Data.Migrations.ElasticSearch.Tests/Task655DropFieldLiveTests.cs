using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Migrations;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.ElasticSearch;
using Birko.Data.Migrations.ElasticSearch.Context;
using Birko.Data.Migrations.ElasticSearch.Settings;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>
/// TASK-655: <c>DropField</c> removes the field from every document's source (live; it was a no-op), and
/// <c>DropIndex</c> refuses, as <c>CreateIndex</c> does since TASK-274, instead of reporting a drop that did not happen.
/// Gated on <c>BIRKO_ES_URL</c> for the live test.
/// </summary>
public sealed class Task655DropFieldLiveTests : IDisposable
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private readonly ElasticClient? _client;
    private readonly string _index = "birko-task655-" + Guid.NewGuid().ToString("N");

    public Task655DropFieldLiveTests(ITestOutputHelper output)
    {
        _output = output;
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live ElasticSearch. Set BIRKO_ES_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return;
        }
        _client = new ElasticClient(new ConnectionSettings(new Uri(Url!)).DisableDirectStreaming());
    }

    public void Dispose()
    {
        try { _client?.Indices.Delete(_index); } catch { }
        try { _client?.Indices.Delete(_index + "-migrations"); } catch { }
    }

    private sealed class DropIndexMigration : AbstractMigration
    {
        private readonly string _index;
        public DropIndexMigration(string index) => _index = index;
        public override long Version => 1;
        public override string Name => "DropIndex";
        public override void Up(IMigrationContext context) => context.Schema.DropIndex(_index, "IX_Code");
    }

    [Fact]
    public void A_migration_that_drops_an_index_fails_and_is_not_recorded()
    {
        if (_client == null) return;
        var runner = new ElasticSearchMigrationRunner(_client, new ElasticSearchMigrationSettings { MigrationsIndex = _index + "-migrations" });
        runner.Initialize();
        runner.RegisterMigrations(new DropIndexMigration(_index));

        MigrationResult? result = null;
        Exception? thrown = null;
        try { result = runner.Migrate(); } catch (Exception ex) { thrown = ex; }
        _output.WriteLine($"result: success={result?.Success} thrown={thrown?.GetType().Name}: {thrown?.Message}");

        (thrown != null || result?.Success == false).Should().BeTrue("a drop that cannot happen must not succeed");
        runner.CurrentVersion.Should().Be(0, "nothing was dropped, so the migration must not be recorded as applied");
    }

    [Fact]
    public void DropField_removes_the_field_from_every_document()
    {
        if (_client == null) return;
        _client.IndexMany(new[]
        {
            new Dictionary<string, object?> { ["Secret"] = "x", ["Keep"] = 1 },
            new Dictionary<string, object?> { ["Keep"] = 2 },
        }, _index);
        _client.Indices.Refresh(_index);

        new ElasticSearchSchemaBuilder(_client).DropField(_index, "Secret");

        var hits = _client.Search<Dictionary<string, object>>(s => s.Index(_index).Size(10)).Documents;
        hits.Should().HaveCount(2).And.OnlyContain(d => !d.ContainsKey("Secret"));
        hits.Select(d => Convert.ToInt32(d["Keep"])).Should().BeEquivalentTo(new[] { 1, 2 });
        _client.Count<object>(c => c.Index(_index).Query(q => q.Exists(e => e.Field("Secret")))).Count
            .Should().Be(0, "the reindexed documents no longer carry the field");
    }

    [Fact]
    public void DropIndex_refuses_instead_of_reporting_a_drop()
    {
        var schema = new ElasticSearchSchemaBuilder(new ElasticClient(new ConnectionSettings(new Uri("http://localhost:1"))));

        FluentActions.Invoking(() => schema.DropIndex("Rows", "IX_Code")).Should().Throw<NotSupportedException>().WithMessage("*ElasticSearch*");
    }
}
