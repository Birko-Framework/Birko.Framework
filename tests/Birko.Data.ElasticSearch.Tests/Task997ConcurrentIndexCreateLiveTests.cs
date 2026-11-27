using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

public class Task997Doc : AbstractModel
{
    public string? Name { get; set; }
}

/// <summary>
/// TASK-997: several stores, like several app instances, starting at once on an index that does not exist yet. Each
/// store's init checked whether the index existed and then created it, so all saw "missing" and every create after the
/// first failed with <c>resource_already_exists_exception</c> (found by TASK-995's two-writers contract test). All of
/// them must start.
/// </summary>
/// <remarks>Gated on <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</remarks>
public sealed class Task997ConcurrentIndexCreateLiveTests : IDisposable
{
    private const int Stores = 8;
    private const int Rounds = 5;
    private readonly ElasticLive _live;

    public Task997ConcurrentIndexCreateLiveTests(ITestOutputHelper output) => _live = new ElasticLive(output);

    public void Dispose() => _live.Dispose();

    [Fact]
    public async Task Async_stores_starting_together_on_a_new_index_all_start()
    {
        if (!_live.Ready()) return;
        for (var round = 0; round < Rounds; round++)
        {
            var settings = _live.NewSettings();
            var stores = Enumerable.Range(0, Stores).Select(_ =>
            {
                var s = new AsyncElasticSearchStore<Task997Doc>();
                s.SetSettings(settings);
                return s;
            }).ToList();
            using var start = new Barrier(Stores);
            var counts = await Task.WhenAll(stores.Select(s => Task.Run(async () =>
            {
                start.SignalAndWait();
                return await s.CountAsync();
            })));
            counts.Should().AllSatisfy(c => c.Should().Be(0));
        }
    }

    [Fact]
    public async Task Sync_stores_starting_together_on_a_new_index_all_start()
    {
        if (!_live.Ready()) return;
        for (var round = 0; round < Rounds; round++)
        {
            var settings = _live.NewSettings();
            var stores = Enumerable.Range(0, Stores).Select(_ =>
            {
                var s = new ElasticSearchStore<Task997Doc>();
                s.SetSettings(settings);
                return s;
            }).ToList();
            using var start = new Barrier(Stores);
            var counts = await Task.WhenAll(stores.Select(s => Task.Run(() =>
            {
                start.SignalAndWait();
                return s.Count();
            })));
            counts.Should().AllSatisfy(c => c.Should().Be(0));
        }
    }
}
