using System;
using System.Linq;
using Birko.Data.Migrations.MongoDB.Context;
using Birko.Data.Patterns.Schema;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.MongoDB.Tests;

/// <summary>
/// TASK-274 — <c>IIndexBuilder.Sparse()</c> on MongoDB, the one backend that can honour it natively.
/// </summary>
/// <remarks>
/// <para>
/// This is the measured two-door disagreement: <c>MongoDBIndexManager</c> sets
/// <c>CreateIndexOptions.Sparse</c> from <c>IndexDefinition.Sparse</c>, while the migration door's
/// <c>Sparse()</c> was <c>=> this</c> and built its options without it. So the same declaration meant
/// different things depending on which door a caller used — the shape TASK-214 (Mongo's id) and TASK-244
/// (the transaction doors) both arrived in.
/// </para>
/// <para>
/// Asserted against the index's own catalogue entry rather than against the absence of an exception: a
/// dropped flag produces a perfectly successful <c>createIndexes</c>, which is exactly why it went unnoticed.
/// </para>
/// <para>Gated on <c>BIRKO_MONGO_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</para>
/// </remarks>
public class SparseIndexBuilderLiveTests : IDisposable
{
    private const string Collection = "SparseProbe";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MONGO_PORT"), out var p) ? p : 27017;
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MONGO_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public SparseIndexBuilderLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private IMongoDatabase Db()
        => new MongoClient($"mongodb://{Host}:{Port}").GetDatabase(Database);

    private ISchemaBuilder Schema() => new MongoSchemaBuilder(Db(), null);

    private BsonDocument? IndexEntry(string name)
        => Db().GetCollection<BsonDocument>(Collection).Indexes.List().ToList()
               .FirstOrDefault(i => i["name"].AsString == name);

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Db().DropCollection(Collection); } catch { }
    }

    [Fact]
    public void A_sparse_declaration_produces_a_sparse_index()
    {
        if (!RequireServer()) return;
        try { Db().DropCollection(Collection); } catch { }

        Schema().CreateIndex(Collection, "ux_sparse_code").WithField("Code").Unique().Sparse().Build();

        var entry = IndexEntry("ux_sparse_code");
        entry.Should().NotBeNull("the index must exist");
        entry!.GetValue("unique", false).ToBoolean().Should().BeTrue();
        entry.GetValue("sparse", false).ToBoolean().Should().BeTrue(
            "TASK-274: Sparse() was dropped here, so this door produced a FULL unique index while the "
          + "index-manager door produced a sparse one");
    }

    /// <summary>
    /// The boundary: without <c>Sparse()</c> the index is not sparse, so the flag is carried rather than
    /// always set.
    /// </summary>
    [Fact]
    public void An_ordinary_declaration_is_not_sparse()
    {
        if (!RequireServer()) return;
        try { Db().DropCollection(Collection); } catch { }

        Schema().CreateIndex(Collection, "ux_plain_code").WithField("Code").Unique().Build();

        var entry = IndexEntry("ux_plain_code");
        entry.Should().NotBeNull();
        entry!.GetValue("sparse", false).ToBoolean().Should().BeFalse();
    }

    /// <summary>
    /// <c>WithProperty</c> is refused: Mongo's index creation here reads name, uniqueness, sparseness and
    /// TTL, so a free-form property would be discarded silently.
    /// </summary>
    [Fact]
    public void An_index_property_is_refused()
    {
        if (!RequireServer()) return;

        Action act = () => Schema().CreateIndex(Collection, "ix_prop").WithField("Code").WithProperty("k", 1);

        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("index property 'k'");
    }
}
