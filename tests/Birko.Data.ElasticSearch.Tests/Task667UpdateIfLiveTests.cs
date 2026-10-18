using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-667, live Elasticsearch: <see cref="AsyncElasticSearchStore{T}.UpdateIfAsync"/> is a compare-and-set. It reads
/// the document realtime (seeing writes the search index has not refreshed), applies the update only where the condition
/// holds, and writes conditioned on <c>_seq_no</c> / <c>_primary_term</c>, so concurrent increments all land. Gated on
/// <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.
/// </summary>
public sealed class Task667UpdateIfLiveTests(ITestOutputHelper output) : IDisposable
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly string _index = "birko667store" + Guid.NewGuid().ToString("N");

    public class Counter : AbstractModel
    {
        public string? State { get; set; }
        public int Hits { get; set; }
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { Http.DeleteAsync(Url.TrimEnd('/') + "/" + _index + "*").GetAwaiter().GetResult(); } catch { }
    }

    private AsyncElasticSearchStore<Counter>? Open()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return null;
        }
        var store = new AsyncElasticSearchStore<Counter>();
        store.SetSettings(new Settings { Location = Url, Name = _index });
        return store;
    }

    [Fact]
    public async Task The_update_sees_an_unrefreshed_write_and_applies_only_where_the_condition_holds()
    {
        if (Open() is not { } store) return;
        var id = await store.CreateAsync(new Counter { State = "open" });

        // No refresh anywhere: a search would still see nothing, the realtime read sees the document.
        (await store.UpdateIfAsync(id, c => c.State == "closed", c => c.Hits = 99)).Should().BeNull();
        (await store.UpdateIfAsync(id, c => c.State == "open", c => { c.State = "closed"; c.Hits = 1; }))!.State.Should().Be("closed");
        (await store.UpdateIfAsync(id, c => c.State == "open", c => c.Hits = 50)).Should().BeNull("the previous write is seen at once");
        (await store.ReadRealtimeAsync(id))!.Hits.Should().Be(1);

        (await store.UpdateIfAsync(Guid.NewGuid(), _ => true, c => c.Hits = 5)).Should().BeNull("a missing document is not created");
    }

    [Fact]
    public async Task Concurrent_conditional_increments_all_land()
    {
        if (Open() is not { } store) return;
        var id = await store.CreateAsync(new Counter { State = "open" });
        const int writers = 8;
        await Task.WhenAll(Enumerable.Range(0, writers).Select(_ => Task.Run(() => store.UpdateIfAsync(id, _ => true, c => c.Hits++))));

        (await store.ReadRealtimeAsync(id))!.Hits.Should().Be(writers, "a conflict is read again, never written over");
    }
}
