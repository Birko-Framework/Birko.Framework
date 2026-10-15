using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Sync.ElasticSearch.Models;
using Birko.Data.Sync.ElasticSearch.Stores;
using Birko.Data.Sync.Models;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Sync.ElasticSearch.Tests;

/// <summary>
/// TASK-157: the sync engine round-tripped against a real ElasticSearch, because the MongoDB knowledge store turned out
/// to be unusable end to end while its offline tests passed. Two runs: the second updates the first run's knowledge.
/// Measured before the fix: the engine's knowledge read (<c>k =&gt; k.Scope == s</c>, written against the generic
/// <c>TKnowledge</c>) bound to <c>ISyncKnowledgeItem.Scope</c>, lost the model's <c>[Keyword(Name = "scope")]</c>, and
/// matched nothing, so every run inserted its knowledge afresh (two rows per entity after two runs).
/// </summary>
/// <remarks>Gated on <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</remarks>
public class ElasticSyncKnowledgeLiveTests
{
    private readonly ITestOutputHelper _output;
    public ElasticSyncKnowledgeLiveTests(ITestOutputHelper output) => _output = output;

    private string? ResolveUrl()
    {
        var url = Environment.GetEnvironmentVariable("BIRKO_ES_URL");
        if (!string.IsNullOrWhiteSpace(url)) return url.TrimEnd('/');
        const string message = "SKIPPED: no live ElasticSearch. Set BIRKO_ES_URL to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
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
        var url = ResolveUrl();
        if (url == null) return;
        var prefix = "birko157" + Guid.NewGuid().ToString("N");
        var local = new AsyncElasticSearchStore<Item>();
        local.SetSettings(new Settings { Location = url, Name = prefix + "l" });
        var remote = new AsyncElasticSearchStore<Item>();
        remote.SetSettings(new Settings { Location = url, Name = prefix + "r" });
        var knowledge = new AsyncElasticSyncKnowledgeStore();
        knowledge.SetSettings(new Settings { Location = url, Name = prefix + "k" });

        using var http = new HttpClient();
        // A search does not see a write until the next refresh; the engine reads by search, so each step refreshes, as
        // the interval between two real sync runs would.
        async Task Refresh() => (await http.PostAsync($"{url}/{prefix}*/_refresh", null)).EnsureSuccessStatusCode();

        try
        {
            var item = new Item { Guid = Guid.NewGuid(), Name = "a", UpdatedAt = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc) };
            await remote.CreateAsync(item);
            await Refresh();
            var provider = new AsyncSyncProvider<AsyncElasticSearchStore<Item>, Item, ElasticSyncKnowledgeItem>(local, remote, knowledge);

            var first = await provider.SyncAsync(new SyncOptions { Scope = "items" });
            first.Errors.Select(e => e.Message + ": " + e.Details).Should().BeEmpty();
            await Refresh();

            var changed = (await remote.ReadAsync(item.Guid!.Value))!;
            changed.Name = "b";
            changed.UpdatedAt = changed.UpdatedAt.AddHours(1);
            await remote.UpdateAsync(changed);
            await Refresh();

            var second = await provider.SyncAsync(new SyncOptions { Scope = "items" });
            await Refresh();

            second.Errors.Select(e => e.Message + ": " + e.Details).Should().BeEmpty();
            second.IsInitialSync.Should().BeFalse();
            (await local.ReadAsync(item.Guid!.Value))!.Name.Should().Be("b");
            var rows = (await knowledge.ReadAsync(x => x.Scope == "items", null, null, null)).Where(x => x.EntityGuid == item.Guid).ToList();
            rows.Should().ContainSingle("the second run updates the row, not a second one");
        }
        finally
        {
            await http.DeleteAsync($"{url}/{prefix}*");
        }
    }
}
