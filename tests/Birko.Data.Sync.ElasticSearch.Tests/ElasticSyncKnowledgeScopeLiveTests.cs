using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Sync.ElasticSearch.Stores;
using Birko.Data.Sync.Models;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Sync.ElasticSearch.Tests;

/// <summary>
/// TASK-613, live: <c>Scope</c> is mapped <c>[Keyword(Name = "scope")]</c>, so it has no <c>scope.keyword</c>
/// sub-field. The translator queried that sub-field and <c>GetLastSyncTimeAsync</c> answered null for every scope —
/// "never synced", so a sync that trusts it re-pulls everything.
/// <para>Gated on <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public sealed class ElasticSyncKnowledgeScopeLiveTests : IDisposable
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private readonly string _prefix = "birko613" + Guid.NewGuid().ToString("N");

    public ElasticSyncKnowledgeScopeLiveTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { Http.DeleteAsync(Url.TrimEnd('/') + "/" + _prefix + "*").GetAwaiter().GetResult(); } catch { }
    }

    [Fact]
    public async Task The_last_sync_time_of_a_scope_is_found()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            _output.WriteLine("SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_ES_URL is not.");
            return;
        }

        var store = new AsyncElasticSyncKnowledgeStore();
        store.SetSettings(new Birko.Data.ElasticSearch.Stores.Settings { Location = Url, Name = _prefix });

        var item = store.CreateKnowledgeItem(Guid.NewGuid(), "h1", "h1", new SyncOptions { Scope = "Products" });
        var at = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        item.LastSyncedAt = at;
        await store.CreateAsync(item, ct: CancellationToken.None);
        (await Http.PostAsync(Url.TrimEnd('/') + "/_refresh", null)).EnsureSuccessStatusCode();

        (await store.GetLastSyncTimeAsync("Products", CancellationToken.None)).Should().Be(at);
        (await store.GetLastSyncTimeAsync("Orders", CancellationToken.None)).Should().BeNull();
    }
}
