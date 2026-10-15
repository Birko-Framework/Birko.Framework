using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.MongoDb.Models;
using Birko.Data.Sync.MongoDb.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Sync.MongoDb.Tests;

/// <summary>
/// SH-M061 (TASK-157), measured against a real MongoDB: the sync engine upserts its knowledge rows, so a second run
/// UPDATES the rows the first run created. The model declared a second <c>[BsonId] string Id</c> beside the framework's
/// canonical id (<c>AbstractModel.Guid</c> is <c>_id</c>, TASK-219), and the harvest claimed the replace was rejected.
/// </summary>
/// <remarks>Gated on <c>BIRKO_MONGO_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</remarks>
public class MongoSyncKnowledgeLiveTests
{
    private const string HostEnv = "BIRKO_MONGO_HOST";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));
    private readonly ITestOutputHelper _output;

    public MongoSyncKnowledgeLiveTests(ITestOutputHelper output) => _output = output;

    private string? ResolveHost()
    {
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (!string.IsNullOrWhiteSpace(host)) return host;
        const string message = "SKIPPED: no live MongoDB. Set " + HostEnv + " to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return null;
    }

    public class Item : AbstractModel
    {
        public string? Name { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    [Fact]
    public async Task A_second_run_updates_the_knowledge_the_first_run_created()
    {
        var host = ResolveHost();
        if (host == null) return;
        var name = "birko_task157_" + Guid.NewGuid().ToString("N");
        var settings = new Settings(host, name);
        var remoteSettings = new Settings(host, name + "_r");
        var local = new AsyncMongoDBStore<Item>();
        local.SetSettings(settings);
        var remote = new AsyncMongoDBStore<Item>();
        remote.SetSettings(remoteSettings);
        var knowledge = new AsyncMongoSyncKnowledgeStore();
        knowledge.SetSettings(settings);

        try
        {
            var item = new Item { Guid = Guid.NewGuid(), Name = "a", UpdatedAt = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc) };
            await remote.CreateAsync(item);
            var provider = new AsyncSyncProvider<AsyncMongoDBStore<Item>, Item, MongoSyncKnowledgeItem>(local, remote, knowledge);

            var first = await provider.SyncAsync(new SyncOptions { Scope = "items" });
            first.Errors.Select(e => e.Message + ": " + e.Details).Should().BeEmpty();

            var changed = (await remote.ReadAsync(item.Guid!.Value))!;
            changed.Name = "b";
            changed.UpdatedAt = changed.UpdatedAt.AddHours(1);
            await remote.UpdateAsync(changed);

            var second = await provider.SyncAsync(new SyncOptions { Scope = "items" });

            second.Errors.Select(e => e.Details).Should().BeEmpty();
            (await local.ReadAsync(item.Guid!.Value))!.Name.Should().Be("b");
            var rows = (await knowledge.ReadAsync(x => x.Scope == "items", null, null, null)).Where(x => x.EntityGuid == item.Guid).ToList();
            rows.Should().ContainSingle("the second run updates the row, not a second one");
            rows[0].RemoteVersion.Should().Be(changed.UpdatedAt.ToString("O"));
        }
        finally
        {
            var client = new Birko.Data.MongoDB.MongoDBClient(settings).Database.Client;
            await client.DropDatabaseAsync(settings.Name);
            await client.DropDatabaseAsync(remoteSettings.Name);
        }
    }
}
