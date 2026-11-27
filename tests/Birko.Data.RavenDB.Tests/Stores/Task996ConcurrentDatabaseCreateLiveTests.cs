using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// TASK-996: several stores, like several app instances, starting at once on a database that does not exist yet. Each
/// store's init checked whether the database existed and then created it, so all saw "missing" and every create after the
/// first failed with "Database … already exists!" (found by TASK-995's two-writers contract test). All of them must start.
/// </summary>
/// <remarks>Gated on <c>BIRKO_RAVEN_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</remarks>
public class Task996ConcurrentDatabaseCreateLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public Task996ConcurrentDatabaseCreateLiveTests(ITestOutputHelper output) => _output = output;

    private const int Stores = 8;
    private const int Rounds = 5;

    public class Item : AbstractModel
    {
        public string? Name { get; set; }
    }

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

    [Fact]
    public async Task Async_stores_starting_together_on_a_new_database_all_start()
    {
        if (!RequireServer()) return;
        for (var round = 0; round < Rounds; round++)
        {
            var db = "birko-task996-" + Guid.NewGuid().ToString("N");
            var stores = Enumerable.Range(0, Stores).Select(_ => new AsyncRavenDBStore<Item>(Url!, db)).ToList();
            try
            {
                using var start = new Barrier(Stores);
                var counts = await Task.WhenAll(stores.Select(s => Task.Run(async () =>
                {
                    start.SignalAndWait();
                    return await s.CountAsync();
                })));
                counts.Should().AllSatisfy(c => c.Should().Be(0));
            }
            finally
            {
                try { await stores[0].DestroyAsync(); } catch { }
                stores.ForEach(s => s.Dispose());
            }
        }
    }

    [Fact]
    public async Task Sync_stores_starting_together_on_a_new_database_all_start()
    {
        if (!RequireServer()) return;
        for (var round = 0; round < Rounds; round++)
        {
            var db = "birko-task996-" + Guid.NewGuid().ToString("N");
            var stores = Enumerable.Range(0, Stores).Select(_ => new RavenDBStore<Item>(Url!, db)).ToList();
            try
            {
                using var start = new Barrier(Stores);
                var counts = await Task.WhenAll(stores.Select(s => Task.Run(() =>
                {
                    start.SignalAndWait();
                    return s.Count();
                })));
                counts.Should().AllSatisfy(c => c.Should().Be(0));
            }
            finally
            {
                try { stores[0].Destroy(); } catch { }
                stores.ForEach(s => s.Dispose());
            }
        }
    }
}
