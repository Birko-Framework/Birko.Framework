using System;
using System.Linq;
using Birko.Data.Migrations.MongoDB.Context;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.MongoDB.Tests;

/// <summary>
/// TASK-655, live: <c>DropField</c> removes the field from every document. It was a no-op, and the migration that called
/// it was recorded as applied. Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public class Task655DropFieldLiveTests : IDisposable
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MONGO_PORT"), out var p) ? p : 27017;

    private readonly ITestOutputHelper _output;
    private readonly IMongoDatabase? _database;

    public Task655DropFieldLiveTests(ITestOutputHelper output)
    {
        _output = output;
        if (string.IsNullOrWhiteSpace(Host))
        {
            const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return;
        }
        _database = new MongoClient($"mongodb://{Host}:{Port}").GetDatabase("birko_task655_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose() => _database?.Client.DropDatabase(_database.DatabaseNamespace.DatabaseName);

    [Fact]
    public void DropField_removes_the_field_from_every_document()
    {
        if (_database == null) return;
        var rows = _database.GetCollection<BsonDocument>("Rows");
        rows.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "Secret", "a" }, { "Keep", 1 } },
            new BsonDocument { { "_id", 2 }, { "Secret", BsonNull.Value }, { "Keep", 2 } },
            new BsonDocument { { "_id", 3 }, { "Keep", 3 } },
        });

        new MongoSchemaBuilder(_database).DropField("Rows", "Secret");

        var all = rows.Find(FilterDefinition<BsonDocument>.Empty).ToList();
        all.Should().HaveCount(3);
        all.Should().OnlyContain(d => !d.Contains("Secret"), "the dropped field is gone from every document, a null one included");
        all.Select(d => d["Keep"].AsInt32).Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }
}
