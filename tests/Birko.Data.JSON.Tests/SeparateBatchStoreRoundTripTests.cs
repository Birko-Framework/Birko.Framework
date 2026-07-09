using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.JSON.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.JSON.Tests;

/// <summary>
/// CR-H050 (separate-store Destroy had an inverted guard and never deleted when configured) and
/// CR-H051 (files saved as "{Name}-{guid}" were reloaded with the bare "{Name}" glob and never
/// re-matched). Round-trips exercise real disk I/O: Create, reload in a fresh store, Destroy.
/// PathValidator rejects absolute Windows paths, so a RELATIVE location is used.
/// </summary>
public class SeparateBatchStoreRoundTripTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public SeparateBatchStoreRoundTripTests()
    {
        _location = $"birko-json-{Guid.NewGuid():N}";
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private Settings NewSettings() => new(_location, "data.json");

    [Fact]
    public void SeparateStore_Create_ReloadsInFreshStore()
    {
        Guid id;
        {
            var store = new JsonSeparateStore<TestModel>();
            store.SetSettings(NewSettings());
            id = store.Create(new TestModel { Name = "alpha", Value = 7 });
        }

        var reloaded = new JsonSeparateStore<TestModel>();
        reloaded.SetSettings(NewSettings());
        var item = reloaded.Read(id);

        item.Should().NotBeNull("CR-H051: a saved entity must be found after reload");
        item!.Name.Should().Be("alpha");
        item.Value.Should().Be(7);
    }

    [Fact]
    public void SeparateStore_Destroy_RemovesPersistedData()
    {
        // CR-H050: Destroy's inverted guard used to return early when the store was configured, so
        // nothing was deleted. Verify behaviorally: after Destroy, a fresh store reloads nothing.
        Guid id;
        {
            var store = new JsonSeparateStore<TestModel>();
            store.SetSettings(NewSettings());
            id = store.Create(new TestModel { Name = "x" });
            store.Destroy();
        }

        var reloaded = new JsonSeparateStore<TestModel>();
        reloaded.SetSettings(NewSettings());

        reloaded.Read(id).Should().BeNull("Destroy must delete the store's persisted files");
    }

    [Fact]
    public void SeparateBulkStore_Create_ReloadsInFreshStore()
    {
        Guid id;
        {
            var store = new JsonSeparateBulkStore<TestModel>();
            store.SetSettings(NewSettings());
            id = store.Create(new TestModel { Name = "bravo", Value = 3 });
        }

        var reloaded = new JsonSeparateBulkStore<TestModel>();
        reloaded.SetSettings(NewSettings());

        reloaded.Read(id).Should().NotBeNull();
    }

    private BatchSettings NewBatchSettings() => new() { Location = _location, Name = "data.json", BatchSize = 2 };

    [Fact]
    public void BatchStore_Create_ReloadsInFreshStore()
    {
        Guid id;
        {
            var store = new JsonBatchStore<TestModel>();
            store.SetSettings(NewBatchSettings());
            id = store.Create(new TestModel { Name = "charlie", Value = 5 });
        }

        var reloaded = new JsonBatchStore<TestModel>();
        reloaded.SetSettings(NewBatchSettings());

        reloaded.Read(id).Should().NotBeNull();
    }

    [Fact]
    public async Task AsyncSeparateStore_Create_ReloadsInFreshStore()
    {
        Guid id;
        {
            var store = new AsyncJsonSeparateStore<TestModel>();
            store.SetSettings(NewSettings());
            id = await store.CreateAsync(new TestModel { Name = "delta", Value = 9 });
        }

        var reloaded = new AsyncJsonSeparateStore<TestModel>();
        reloaded.SetSettings(NewSettings());

        (await reloaded.ReadAsync(id)).Should().NotBeNull();
    }
}
