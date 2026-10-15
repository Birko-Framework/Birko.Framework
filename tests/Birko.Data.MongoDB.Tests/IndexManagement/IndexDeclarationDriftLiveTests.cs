using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.MongoDB.IndexManagement;
using Birko.Data.Patterns.IndexManagement;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.IndexManagement;

/// <summary>
/// TASK-635, live MongoDB: declared indexes compared with what <see cref="MongoDBIndexManager"/> reports.
/// Gated on <c>BIRKO_MONGO_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.
/// </summary>
public class IndexDeclarationDriftLiveTests
{
    private readonly ITestOutputHelper _output;
    public IndexDeclarationDriftLiveTests(ITestOutputHelper output) => _output = output;

    private string? Host()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        if (!string.IsNullOrWhiteSpace(host)) return host;
        const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return null;
    }

    private static readonly IndexDefinition[] Declared =
    {
        new() { Name = "UX_Number", Fields = new[] { IndexField.Ascending("Number") }, Unique = true },
        new() { Name = "IX_AB", Fields = new[] { IndexField.Ascending("A"), IndexField.Descending("B") } },
        new() { Name = "IX_Search", Fields = new[] { IndexField.Text("Name"), IndexField.Text("Notes") } },
    };

    [Fact]
    public async Task Indexes_created_from_the_declarations_are_clean_and_each_kind_of_drift_is_reported()
    {
        var host = Host();
        if (host == null) return;
        var settings = new Birko.Data.MongoDB.Stores.Settings(host, "birko_task635_" + Guid.NewGuid().ToString("N"));
        var client = new MongoDBClient(settings);
        var manager = new MongoDBIndexManager(client);
        try
        {
            foreach (var d in Declared) await manager.CreateAsync(d, "Invoices");

            var clean = await IndexDeclarationDriftDetector.DetectAsync(manager, Declared, "Invoices");
            foreach (var u in clean.Unverified) _output.WriteLine(u.ToString());
            foreach (var d in clean.Drifts) _output.WriteLine(d.ToString());
            clean.IsClean.Should().BeTrue("indexes created from the declarations, text index included, match them");

            var collection = client.Database.GetCollection<BsonDocument>("Invoices");
            await manager.DropAsync("IX_AB", "Invoices");
            await manager.DropAsync("UX_Number", "Invoices");
            await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("Number"), new CreateIndexOptions { Name = "UX_Number" }));

            var drifted = await IndexDeclarationDriftDetector.DetectAsync(manager, Declared.Append(
                new IndexDefinition { Name = "IX_Search", Fields = new[] { IndexField.Text("Name") } }).Skip(1), "Invoices");

            drifted.Drifts.Select(d => (d.Index, d.Kind)).Should().BeEquivalentTo(new[]
            {
                ("IX_AB", IndexDeclarationDriftKind.Missing),
                ("IX_Search", IndexDeclarationDriftKind.ColumnsDiffer),
            }, "the second IX_Search declaration names one text field where the index has two");
            var uniqueness = await IndexDeclarationDriftDetector.DetectAsync(manager, Declared.Take(1), "Invoices");
            uniqueness.Drifts.Should().ContainSingle().Which.Kind.Should().Be(IndexDeclarationDriftKind.UniquenessDiffers,
                "UX_Number is declared unique and stored plain: duplicates are accepted");
        }
        finally
        {
            await client.Database.Client.DropDatabaseAsync(settings.Name);
        }
    }
}
