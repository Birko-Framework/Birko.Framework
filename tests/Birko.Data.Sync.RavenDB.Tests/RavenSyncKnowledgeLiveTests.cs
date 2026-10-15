using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.RavenDB.Models;
using Birko.Data.Sync.RavenDB.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Sync.RavenDB.Tests;

/// <summary>
/// TASK-630: the sync engine round-tripped against a real RavenDB through the rebuilt knowledge store. Two runs: the
/// second updates the first run's knowledge row (one row per entity), which the old store's own upsert path did not
/// do (SH-M062: each round stored a new document).
/// </summary>
/// <remarks>Gated on <c>BIRKO_RAVEN_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</remarks>
public class RavenSyncKnowledgeLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public RavenSyncKnowledgeLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL (e.g. http://localhost:8080); "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public class Item : AbstractModel
    {
        public string? Name { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // RavenDB queries read indexes, which catch up asynchronously; the gap stands in for the time between two real runs.
    private static Task Settle() => Task.Delay(1500);

    [Fact]
    public async Task A_second_run_updates_the_knowledge_the_first_run_created()
    {
        if (!RequireServer()) return;
        var db = "birko-task630-" + Guid.NewGuid().ToString("N");
        using var local = new AsyncRavenDBStore<Item>(Url!, db + "-l");
        using var remote = new AsyncRavenDBStore<Item>(Url!, db + "-r");
        using var knowledge = new AsyncRavenSyncKnowledgeStore(Url!, db + "-k");
        try
        {
            var item = new Item { Guid = Guid.NewGuid(), Name = "a", UpdatedAt = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc) };
            await remote.CreateAsync(item);
            await Settle();
            var provider = new AsyncSyncProvider<AsyncRavenDBStore<Item>, Item, RavenSyncKnowledgeItem>(local, remote, knowledge);

            var first = await provider.SyncAsync(new SyncOptions { Scope = "items" });
            first.Errors.Select(e => e.Message + ": " + e.Details).Should().BeEmpty();
            await Settle();

            var changed = (await remote.ReadAsync(item.Guid!.Value, CancellationToken.None))!;
            changed.Name = "b";
            changed.UpdatedAt = changed.UpdatedAt.AddHours(1);
            await remote.UpdateAsync(changed);
            await Settle();

            var second = await provider.SyncAsync(new SyncOptions { Scope = "items" });
            await Settle();

            second.Errors.Select(e => e.Message + ": " + e.Details).Should().BeEmpty();
            second.IsInitialSync.Should().BeFalse();
            (await local.ReadAsync(item.Guid!.Value, CancellationToken.None))!.Name.Should().Be("b");
            var rows = (await knowledge.ReadAsync(x => x.Scope == "items", null, null, null)).Where(x => x.EntityGuid == item.Guid).ToList();
            rows.Should().ContainSingle("the second run updates the row, not a second one");
            rows[0].RemoteVersion.Should().Be(changed.UpdatedAt.ToString("O"));
        }
        finally
        {
            try { await local.DestroyAsync(); } catch { }
            try { await remote.DestroyAsync(); } catch { }
            try { await knowledge.DestroyAsync(); } catch { }
        }
    }
}
