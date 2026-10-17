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
/// TASK-636, live MongoDB: a partial unique index is declared through <see cref="IndexDefinition.WhereNotNull"/> /
/// <see cref="IndexDefinition.WhereNull"/>, created, reported, compared for drift and dropped. Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public class Task636MongoPartialIndexLiveTests
{
    private readonly ITestOutputHelper _output;
    public Task636MongoPartialIndexLiveTests(ITestOutputHelper output) => _output = output;

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

    private static readonly IndexDefinition DefaultMarker = new()
    {
        Name = "UX_Default",
        Fields = new[] { IndexField.Ascending("DefaultMarker") },
        Unique = true,
        WhereNotNull = new[] { "DefaultMarker" },
    };

    private static readonly IndexDefinition LiveSlug = new()
    {
        Name = "UX_LiveSlug",
        Fields = new[] { IndexField.Ascending("Slug") },
        Unique = true,
        WhereNull = new[] { "DeletedAt" },
    };

    private static async Task<bool> Accepted(IMongoCollection<BsonDocument> collection, BsonDocument document)
    {
        try { await collection.InsertOneAsync(document); return true; }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }

    [Fact]
    public async Task At_most_one_default_is_declared_created_reported_and_enforced()
    {
        var host = Host();
        if (host == null) return;
        var client = new MongoDBClient(new Birko.Data.MongoDB.Stores.Settings(host, "birko_task636_" + Guid.NewGuid().ToString("N")));
        var manager = new MongoDBIndexManager(client);
        try
        {
            await manager.CreateAsync(DefaultMarker, "Currencies");
            var rows = client.Database.GetCollection<BsonDocument>("Currencies");

            (await Accepted(rows, new BsonDocument { { "_id", 1 }, { "DefaultMarker", BsonNull.Value } })).Should().BeTrue();
            (await Accepted(rows, new BsonDocument { { "_id", 2 }, { "DefaultMarker", BsonNull.Value } })).Should().BeTrue("a null marker is not indexed");
            (await Accepted(rows, new BsonDocument { { "_id", 3 }, { "DefaultMarker", true } })).Should().BeTrue();
            (await Accepted(rows, new BsonDocument { { "_id", 4 }, { "DefaultMarker", true } })).Should().BeFalse("a second default is refused");

            var info = (await manager.GetInfoAsync("UX_Default", "Currencies"))!;
            info.WhereNotNull.Should().Equal("DefaultMarker");
            info.WhereNull.Should().BeEmpty();
            info.UnrecognizedPredicate.Should().BeNull();
            (await IndexDeclarationDriftDetector.DetectAsync(manager, new[] { DefaultMarker }, "Currencies")).IsClean.Should().BeTrue();

            await manager.DropAsync("UX_Default", "Currencies");
            (await manager.ExistsAsync("UX_Default", "Currencies")).Should().BeFalse();
        }
        finally
        {
            await client.Database.Client.DropDatabaseAsync(client.Database.DatabaseNamespace.DatabaseName);
        }
    }

    [Fact]
    public async Task A_live_rows_only_unique_index_and_a_full_index_read_as_predicate_drift()
    {
        var host = Host();
        if (host == null) return;
        var client = new MongoDBClient(new Birko.Data.MongoDB.Stores.Settings(host, "birko_task636_" + Guid.NewGuid().ToString("N")));
        var manager = new MongoDBIndexManager(client);
        try
        {
            await manager.CreateAsync(LiveSlug, "Pages");
            var rows = client.Database.GetCollection<BsonDocument>("Pages");
            (await Accepted(rows, new BsonDocument { { "_id", 1 }, { "Slug", "w" }, { "DeletedAt", BsonNull.Value } })).Should().BeTrue();
            (await Accepted(rows, new BsonDocument { { "_id", 2 }, { "Slug", "w" }, { "DeletedAt", DateTime.UtcNow } })).Should().BeTrue("a deleted row is not indexed");
            (await Accepted(rows, new BsonDocument { { "_id", 3 }, { "Slug", "w" }, { "DeletedAt", BsonNull.Value } })).Should().BeFalse();
            (await manager.GetInfoAsync("UX_LiveSlug", "Pages"))!.WhereNull.Should().Equal("DeletedAt");

            await manager.DropAsync("UX_LiveSlug", "Pages");
            await manager.CreateAsync(new IndexDefinition { Name = "UX_LiveSlug", Fields = LiveSlug.Fields, Unique = true }, "Pages2");
            var drift = await IndexDeclarationDriftDetector.DetectAsync(manager, new[] { LiveSlug }, "Pages2");
            drift.Drifts.Should().ContainSingle(d => d.Kind == IndexDeclarationDriftKind.PredicateDiffers)
                .Which.ToString().Should().Contain("WHERE DeletedAt IS NULL");
        }
        finally
        {
            await client.Database.Client.DropDatabaseAsync(client.Database.DatabaseNamespace.DatabaseName);
        }
    }

    [Fact]
    public async Task Sparse_and_partial_together_are_refused_before_the_server_is_asked()
    {
        var manager = new MongoDBIndexManager(new MongoDBClient(new Birko.Data.MongoDB.Stores.Settings("127.0.0.1", "none")));
        var definition = new IndexDefinition { Name = "IX", Fields = DefaultMarker.Fields, Sparse = true, WhereNotNull = new[] { "DefaultMarker" } };

        await manager.Invoking(m => m.CreateAsync(definition, "Rows")).Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void The_filter_takes_one_term_per_field_under_and()
    {
        var both = new IndexDefinition { Name = "IX", Fields = DefaultMarker.Fields, WhereNotNull = new[] { "TenantGuid" }, WhereNull = new[] { "DeletedAt" } };

        var filter = MongoDBIndexManager.PartialFilter(both);

        filter["$and"].AsBsonArray.Should().HaveCount(2);
        MongoDBIndexManager.PartialFilter(DefaultMarker).Names.Should().Equal("DefaultMarker");
    }
}
