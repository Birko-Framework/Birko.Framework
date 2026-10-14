using System;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-616, live: <c>ReadRealtimeAsync</c> sees a write before any refresh. A search does not, and a caller that
/// verified its own write with one (the job queue's claim) read the stale document every time.
/// </summary>
public sealed class ReadRealtimeTests : IDisposable
{
    private readonly ElasticLive _live;
    public ReadRealtimeTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    public void Dispose() => _live.Dispose();

    [Fact]
    public async Task A_realtime_read_sees_a_write_the_index_has_not_refreshed()
    {
        if (!_live.Ready()) return;
        var store = new AsyncElasticSearchStore<UpdateMissDoc>();
        store.SetSettings(_live.NewSettings());

        var guid = await store.CreateAsync(new UpdateMissDoc { Name = "before" });
        ElasticLive.Refresh();
        await store.UpdateAsync(x => x.Guid == guid, new Birko.Data.Stores.PropertyUpdate<UpdateMissDoc>().Set(x => x.Name, "after"));

        (await store.ReadRealtimeAsync(guid))!.Name.Should().Be("after");
        (await store.ReadRealtimeAsync(guid))!.Guid.Should().Be(guid);
        (await store.ReadRealtimeAsync(Guid.NewGuid())).Should().BeNull();
    }
}
