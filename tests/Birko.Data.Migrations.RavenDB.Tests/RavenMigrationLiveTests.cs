using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.RavenDB.Context;
using Birko.Data.RavenDB.IndexManagement;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.RavenDB.Tests;

/// <summary>
/// TASK-152, live RavenDB: a migration's index is created under its name (SH-M200), two concurrent recorders both keep
/// their version (SH-M187), and the filter terms reach RQL with their meaning (SH-M175 / SH-M176).
/// Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public sealed class RavenMigrationLiveTests : IDisposable
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    private readonly IDocumentStore? _store;
    private readonly string _db = "birko-task152-" + Guid.NewGuid().ToString("N");

    public RavenMigrationLiveTests(ITestOutputHelper output)
    {
        _output = output;
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return;
        }
        _store = new DocumentStore { Urls = new[] { Url! }, Database = _db }.Initialize();
        _store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(_db)));
    }

    public void Dispose()
    {
        if (_store == null) return;
        try { _store.Maintenance.Server.Send(new DeleteDatabasesOperation(_db, hardDelete: true)); } catch { }
        _store.Dispose();
    }

    private sealed class Mig : AbstractMigration
    {
        public Mig(long version) => Version = version;
        public override long Version { get; }
        public override string Name => "M" + Version;
        public override void Up(IMigrationContext context) { }
    }

    [Fact]
    public async Task A_migration_index_is_created_under_its_name()
    {
        if (_store == null) return;
        var schema = new RavenDBSchemaBuilder(_store);

        schema.CreateIndex("Invoices", "IX_Code").WithField("Code").Build();

        (await new RavenDBIndexManager(_store).ExistsAsync("IX_Code")).Should().BeTrue();
    }

    [Fact]
    public void Two_concurrent_recorders_both_keep_their_version()
    {
        if (_store == null) return;
        var settings = new Settings.RavenMigrationSettings();
        new RavenMigrationStore(_store, settings).Initialize();

        Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var store = new RavenMigrationStore(_store, settings);
            store.Initialize();
            store.RecordMigration(new Mig(i + 1));
        });

        new RavenMigrationStore(_store, settings).GetAppliedVersions().Should().BeEquivalentTo(Enumerable.Range(1, 20).Select(i => (long)i));
    }

    [Fact]
    public void Null_and_not_equal_match_what_the_filter_means()
    {
        if (_store == null) return;
        var data = new RavenDBDataMigrator(_store);
        data.BulkInsert("Rows", new[]
        {
            new System.Collections.Generic.Dictionary<string, object> { ["Kind"] = "a" },
            new System.Collections.Generic.Dictionary<string, object> { ["Kind"] = "b" },
            new System.Collections.Generic.Dictionary<string, object> { ["Other"] = 1 },
        });

        _output.WriteLine($"Kind != a: {data.CountDocuments("Rows", "{\"Kind\":{\"$ne\":\"a\"}}")}");
        data.CountDocuments("Rows", "{\"Kind\":{\"$ne\":\"a\"}}").Should().Be(2, "a document without Kind is not 'a' either");
        data.CountDocuments("Rows", "{\"Kind\":null}").Should().Be(1, "the document without Kind");
        data.CountDocuments("Rows", "{\"Kind\":{\"$ne\":null}}").Should().Be(2);
        data.DeleteDocuments("Rows", "{\"Kind\":null}");
        data.CountDocuments("Rows").Should().Be(2, "the RQL delete path took the same document");
        data.Invoking(d => d.CountDocuments("Rows", "{\"Kind\":{\"$in\":[\"a\",\"b\"]}}")).Should().Throw<NotSupportedException>();
    }
}
