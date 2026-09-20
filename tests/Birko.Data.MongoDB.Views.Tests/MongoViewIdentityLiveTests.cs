using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.Views;
using Birko.Data.Views;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Views.Tests;

/// <summary>
/// TASK-219 — the framework held two contradictory answers for what <c>_id</c> is.
/// <c>MongoSerialization</c> said it was a driver-generated ObjectId beside an ordinary <c>Guid</c>
/// field; <c>MongoViewTranslator.GetFieldName</c> rewrote the <c>Guid</c> property to <c>_id</c>.
/// Under the former, measured against MongoDB 7:
/// <list type="bullet">
/// <item>a view projecting the canonical id into a <c>Guid</c> property <b>threw</b>
/// <c>Cannot deserialize a 'Guid' from BsonType 'ObjectId'</c>;</item>
/// <item>a view filtering on it returned <b>0 rows</b> for a document that exists — silently, which
/// is indistinguishable from "no matches" in a log.</item>
/// </list>
/// Settled in the translator's favour: the canonical Guid IS <c>_id</c>, stored as a string.
///
/// Gated on <c>BIRKO_MONGO_HOST</c> (e.g. <c>localhost</c>); no-op pass when absent so CI stays green.
/// The non-gated half — that the class map mirrors the projection — is in
/// <c>MongoViewSerializationTests</c>.
/// </summary>
public class MongoViewIdentityLiveTests
{
    private const string HostEnv = "BIRKO_MONGO_HOST";

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public MongoViewIdentityLiveTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The configured MongoDB host, or <c>null</c> after reporting a skip.
    /// </summary>
    /// <remarks>
    /// ⚠ TASK-479: each test used to read the variable inline and answer an absent one with a bare
    /// <c>return;</c>, so with no server this file reported <b>Passed</b> rather than Skipped and
    /// <c>BIRKO_REQUIRE_LIVE</c> never saw it — the state <c>live-tests.yml</c>'s header forbids,
    /// because it makes a broken fixture and a real run look identical.
    /// </remarks>
    private string? ResolveHost()
    {
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (!string.IsNullOrWhiteSpace(host))
        {
            return host;
        }

        const string message = "SKIPPED: no live MongoDB. Set " + HostEnv + " to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return null;
    }

    public class Cust : AbstractModel { public string? Name { get; set; } }

    public class CustView { public Guid? EntityKey { get; set; } public string? Name { get; set; } }

    private static ViewDefinition Definition() => new ViewDefinitionBuilder<CustView>()
        .From<Cust>()
        .Select<Cust, Guid?>(c => c.Guid, v => v.EntityKey)
        .Select<Cust, string?>(c => c.Name, v => v.Name)
        .Build();

    [Fact]
    public async Task A_view_projects_and_filters_on_the_canonical_guid()
    {
        var host = ResolveHost();
        if (host == null) return;

        var settings = new Settings(host, "birko_task219_" + Guid.NewGuid().ToString("N"));
        var client = new Birko.Data.MongoDB.MongoDBClient(settings);

        var store = new AsyncMongoDBStore<Cust>();
        store.SetSettings(settings);

        try
        {
            var wanted = new Cust { Name = "acme" };
            await store.CreateAsync(wanted);
            await store.CreateAsync(new Cust { Name = "other" });

            // The stored document carries ONE identity, as a string — no ObjectId beside it.
            var raw = await client.Database.GetCollection<BsonDocument>("Cust")
                .Find(Builders<BsonDocument>.Filter.Eq("Name", "acme")).SingleAsync();
            raw["_id"].BsonType.Should().Be(BsonType.String);
            raw["_id"].AsString.Should().Be(wanted.Guid!.Value.ToString());
            raw.Contains("Guid").Should().BeFalse("the canonical id is _id, not a second element");

            var views = new MongoViewStore<CustView>(client, Definition());

            // Projection: this threw "Cannot deserialize a 'Guid' from BsonType 'ObjectId'".
            var all = (await views.QueryAsync()).ToList();
            all.Should().HaveCount(2);
            all.Should().Contain(v => v.EntityKey == wanted.Guid && v.Name == "acme");

            // Filter: this returned 0 — the silent half, and the reason this was ranked P1.
            var filtered = (await views.QueryAsync(v => v.EntityKey == wanted.Guid)).ToList();
            filtered.Should().ContainSingle();
            filtered[0].Name.Should().Be("acme");

            (await views.CountAsync(v => v.EntityKey == wanted.Guid)).Should().Be(1);
            (await views.CountAsync(v => v.EntityKey == Guid.NewGuid())).Should().Be(0,
                "a genuinely absent id must still return nothing — the fix must not match everything");
        }
        finally
        {
            await client.Database.Client.DropDatabaseAsync(settings.Name);
        }
    }

    [Fact]
    public async Task The_store_still_reads_back_an_entity_by_its_guid()
    {
        var host = ResolveHost();
        if (host == null) return;

        // Moving the canonical id to _id changes how every entity filter renders, so pin the
        // ordinary store path too — not only the view path this task is about.
        var settings = new Settings(host, "birko_task219_store_" + Guid.NewGuid().ToString("N"));
        var store = new AsyncMongoDBStore<Cust>();
        store.SetSettings(settings);

        try
        {
            var entity = new Cust { Name = "acme" };
            await store.CreateAsync(entity);

            var read = await store.ReadAsync(x => x.Guid == entity.Guid);
            read.Should().ContainSingle().Which.Name.Should().Be("acme");
        }
        finally
        {
            await new Birko.Data.MongoDB.MongoDBClient(settings)
                .Database.Client.DropDatabaseAsync(settings.Name);
        }
    }
}
