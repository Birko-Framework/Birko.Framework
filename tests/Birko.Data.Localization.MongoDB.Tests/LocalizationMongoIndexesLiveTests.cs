using System.Linq.Expressions;
using Birko.Data.Exceptions;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Stores;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Localization.MongoDB.Tests;

/// <summary>
/// TASK-699, live: the translation key and ValueHash indexes on MongoDB. Gated on <c>BIRKO_MONGO_HOST</c>
/// (live-tests.yml); <c>BIRKO_REQUIRE_LIVE</c> makes its absence a failure. Each test owns a fresh database.
/// </summary>
public sealed class LocalizationMongoIndexesLiveTests : IDisposable
{
    private const string HostEnv = "BIRKO_MONGO_HOST";
    private readonly ITestOutputHelper _output;
    private readonly List<string> _databases = new();
    private string? _host;

    public LocalizationMongoIndexesLiveTests(ITestOutputHelper output) => _output = output;

    private bool Ready()
    {
        _host = Environment.GetEnvironmentVariable(HostEnv);
        if (!string.IsNullOrWhiteSpace(_host)) return true;
        const string message = "SKIPPED: no live MongoDB. Set " + HostEnv + " to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private Settings NewSettings()
    {
        var name = "birko_task699_" + Guid.NewGuid().ToString("N");
        _databases.Add(name);
        return new Settings(_host!, name);
    }

    private static string Collection => nameof(EntityTranslationModel);

    private AsyncMongoDBStore<EntityTranslationModel> NewStore(Settings settings)
    {
        var store = new AsyncMongoDBStore<EntityTranslationModel>();
        store.SetSettings(settings);
        return store;
    }

    private IMongoCollection<BsonDocument> Raw(Settings settings)
        => new MongoClient($"mongodb://{_host}:27017").GetDatabase(settings.Name).GetCollection<BsonDocument>(Collection);

    private static EntityTranslationModel Row(Guid entity, string value, Guid? guid = null) => new()
    {
        Guid = guid ?? Guid.NewGuid(),
        EntityGuid = entity,
        EntityType = "Product",
        FieldName = "Name",
        Culture = "sk",
        Value = value,
        ValueHash = LocalizedValueHash.Of(value),
    };

    // The winning plan's stages, flattened: COLLSCAN without an index, IXSCAN with one.
    private static async Task<string> PlanAsync(Settings settings, string host, string valueHash)
    {
        var db = new MongoClient($"mongodb://{host}:27017").GetDatabase(settings.Name);
        var explain = await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument { { "find", Collection }, { "filter", new BsonDocument("ValueHash", valueHash) } } },
            { "verbosity", "queryPlanner" },
        });
        return explain["queryPlanner"]["winningPlan"].ToJson();
    }

    [Fact]
    public async Task Before_the_indexes_a_value_lookup_scans_and_after_it_uses_the_index()
    {
        if (!Ready()) return;
        var settings = NewSettings();
        var store = NewStore(settings);
        await store.CreateAsync(Row(Guid.NewGuid(), "hello"));
        var hash = LocalizedValueHash.Of("hello");

        var before = await PlanAsync(settings, _host!, hash);
        await LocalizationMongoIndexes.EnsureAsync(store);
        var after = await PlanAsync(settings, _host!, hash);

        _output.WriteLine("before: " + before);
        _output.WriteLine("after: " + after);
        before.Should().Contain("COLLSCAN", "measured: no index, so the lookup reads every row");
        after.Should().Contain("IXSCAN").And.Contain(LocalizationMongoIndexes.ValueHashIndexName(Collection));
    }

    [Fact]
    public async Task Both_indexes_exist_on_the_server_and_the_key_is_unique()
    {
        if (!Ready()) return;
        var settings = NewSettings();
        var store = NewStore(settings);

        await LocalizationMongoIndexes.EnsureAsync(store);
        await LocalizationMongoIndexes.EnsureAsync(store);

        var indexes = (await (await Raw(settings).Indexes.ListAsync()).ToListAsync()).ToDictionary(i => i["name"].AsString);
        indexes.Should().ContainKey(LocalizationMongoIndexes.ValueHashIndexName(Collection));
        var key = indexes[LocalizationMongoIndexes.KeyIndexName(Collection)];
        key.GetValue("unique", false).ToBoolean().Should().BeTrue();
        key["key"].AsBsonDocument.Names.Should().Equal("EntityGuid", "EntityType", "FieldName", "Culture");
    }

    [Fact]
    public async Task A_duplicate_key_under_another_guid_is_refused_typed()
    {
        if (!Ready()) return;
        var store = NewStore(NewSettings());
        await LocalizationMongoIndexes.EnsureAsync(store);
        var entity = Guid.NewGuid();
        await store.CreateAsync(Row(entity, "first"));

        var duplicate = () => store.CreateAsync(Row(entity, "second"));

        await duplicate.Should().ThrowAsync<StoreUniqueViolationException>("the unique key refuses it, whatever the Guid");
    }

    /// <summary>Lets a writer that does not derive the key Guid (code from before TASK-698) store the key first.</summary>
    private sealed class RacingStore : AsyncMongoDBStore<EntityTranslationModel>
    {
        public Func<Task>? Race { get; set; }

        public override async Task<IEnumerable<EntityTranslationModel>> ReadAsync(
            Expression<Func<EntityTranslationModel, bool>>? filter = null, OrderBy<EntityTranslationModel>? orderBy = null,
            int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            var result = (await base.ReadAsync(filter, orderBy, limit, offset, ct)).ToList();
            var race = Race;
            Race = null;
            if (race != null) await race();
            return result;
        }
    }

    [Fact]
    public async Task With_the_key_index_a_race_against_a_random_guid_writer_leaves_one_row()
    {
        if (!Ready()) return;
        var settings = NewSettings();
        var plain = NewStore(settings);
        await LocalizationMongoIndexes.EnsureAsync(plain);
        var entity = Guid.NewGuid();
        var racing = new RacingStore { Race = () => plain.CreateAsync(Row(entity, "legacy writer")) };
        racing.SetSettings(settings);

        await LocalizedTranslationRows.SaveAsync(racing, entity, "Product", "sk",
            new Dictionary<string, string?> { ["Name"] = "ours" }, CancellationToken.None);

        // The key Guid alone would not collide with a random-Guid row; the unique index does.
        (await plain.ReadAsync(t => t.EntityGuid == entity, null, null, null)).Select(t => t.Value).Should().Equal("ours");
    }

    [Fact]
    public async Task A_collection_already_holding_a_duplicate_key_is_refused_with_the_remedy_in_the_docs()
    {
        if (!Ready()) return;
        var store = NewStore(NewSettings());
        var entity = Guid.NewGuid();
        await store.CreateAsync(Row(entity, "first"));
        await store.CreateAsync(Row(entity, "second"));

        var ensure = () => LocalizationMongoIndexes.EnsureAsync(store);

        await ensure.Should().ThrowAsync<IndexManagementException>("MongoDB cannot build a unique index over a duplicate key");
    }

    [Fact]
    public async Task A_store_without_settings_is_refused()
    {
        var ensure = () => LocalizationMongoIndexes.EnsureAsync(new AsyncMongoDBStore<EntityTranslationModel>());

        await ensure.Should().ThrowAsync<InvalidOperationException>();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(_host) || _databases.Count == 0) return;
        try
        {
            var client = new MongoClient($"mongodb://{_host}:27017");
            foreach (var db in _databases) client.DropDatabase(db);
        }
        catch { /* cleanup is best-effort */ }
    }
}
