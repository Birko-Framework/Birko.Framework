using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.JSON.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.JSON.Tests;

/// <summary>
/// TASK-811. Measured before the fix: the sync <c>JsonSeparateStore</c> and <c>JsonBatchStore</c> kept their files in
/// <c>Location/Name/</c> — <c>Path</c>, which is the single-file <c>JsonStore</c>'s data file — while the async stores and
/// every bulk variant kept them in <c>Location/</c>, so a sync and an async store on one settings did not see each other's
/// records. And in <c>Location/</c> a store found its files by <c>{Name}-*</c>, so a store named <c>items</c> also read,
/// rewrote and destroyed the files of one named <c>items-archive</c>. Decided 2026-11-04 (the user): one flat layout,
/// <c>Location/</c>, and a file is a store's only when it is <c>{Name}-</c> followed by exactly one Guid.
/// </summary>
public class Task811JsonLayoutTests : IDisposable
{
    private readonly string _location = $"birko-json-811-{Guid.NewGuid():N}";
    private string Dir => Path.GetFullPath(_location);

    public Task811JsonLayoutTests() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); } catch { }
    }

    public static TheoryData<string> Kinds => new() { "separate", "separate-bulk", "batch", "batch-bulk" };

    private Settings For(string kind, string name)
        => kind.StartsWith("batch") ? new BatchSettings { Location = _location, Name = name, BatchSize = 2 } : new Settings(_location, name);

    private JsonStore<TestModel> Sync(string kind, string name)
    {
        JsonStore<TestModel> store = kind switch
        {
            "separate" => new JsonSeparateStore<TestModel>(),
            "separate-bulk" => new JsonSeparateBulkStore<TestModel>(),
            "batch" => new JsonBatchStore<TestModel>(),
            _ => new JsonBatchBulkStore<TestModel>(),
        };
        store.SetSettings(For(kind, name));
        return store;
    }

    private AsyncJsonStore<TestModel> Async(string kind, string name)
    {
        AsyncJsonStore<TestModel> store = kind switch
        {
            "separate" => new AsyncJsonSeparateStore<TestModel>(),
            "separate-bulk" => new AsyncJsonSeparateBulkStore<TestModel>(),
            "batch" => new AsyncJsonBatchStore<TestModel>(),
            _ => new AsyncJsonBatchBulkStore<TestModel>(),
        };
        store.SetSettings(For(kind, name));
        return store;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_sync_store_keeps_its_files_in_the_location(string kind)
    {
        Sync(kind, "items").Create(new TestModel { Name = "a" });

        Directory.GetFiles(Dir, "items-*").Should().NotBeEmpty();
        Directory.Exists(Path.Combine(Dir, "items")).Should().BeFalse("no folder named after the store");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_sync_and_an_async_store_on_one_settings_see_each_other_s_records(string kind)
    {
        Sync(kind, "items").Create(new TestModel { Name = "from-sync" });
        await Async(kind, "items").CreateAsync(new TestModel { Name = "from-async" });

        Sync(kind, "items").Count().Should().Be(2);
        (await Async(kind, "items").CountAsync()).Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Stores_whose_names_overlap_keep_to_their_own_records(string kind)
    {
        Sync(kind, "items").Create(new TestModel { Name = "item" });
        Sync(kind, "items-archive").Create(new TestModel { Name = "archived" });

        Sync(kind, "items").Count().Should().Be(1);
        (await Async(kind, "items").CountAsync()).Should().Be(1);

        Sync(kind, "items").Destroy();

        Sync(kind, "items-archive").Count().Should().Be(1, "destroying items leaves items-archive's records");
        (await Async(kind, "items-archive").ReadAsync(x => x.Name == "archived")).Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task An_async_store_s_destroy_leaves_an_overlapping_store_s_records(string kind)
    {
        await Async(kind, "items").CreateAsync(new TestModel { Name = "item" });
        await Async(kind, "items-archive").CreateAsync(new TestModel { Name = "archived" });

        await Async(kind, "items").DestroyAsync();

        (await Async(kind, "items-archive").CountAsync()).Should().Be(1);
    }
}
