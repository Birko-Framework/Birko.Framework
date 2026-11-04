using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.JSON.Stores;
using Birko.Serialization;
using Birko.Serialization.Json;
using FluentAssertions;
using Xunit;

namespace Birko.Data.JSON.Tests;

/// <summary>
/// TASK-721. Measured before the fix: the separate and batch stores' Destroy deleted their own files and then threw
/// <c>IOException</c> when anything else was in the directory; every JSON save deleted the file before writing, so a
/// failed write lost the data; the async loader threw on a null record; the async store re-read the disk on every call
/// while it held no rows.
/// PathValidator rejects absolute Windows paths, so a RELATIVE location is used.
/// </summary>
public class Task721JsonStoreSafetyTests : IDisposable
{
    private readonly string _location = $"birko-json-721-{Guid.NewGuid():N}";
    private string Dir => Path.GetFullPath(_location);

    public Task721JsonStoreSafetyTests() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); } catch { }
    }

    // ---- Destroy keeps what is not the store's ----

    public static TheoryData<string> DestroyKinds => new() { "separate", "separate-bulk", "batch", "batch-bulk" };

    private (JsonStore<TestModel> store, string directory) SyncStore(string kind)
    {
        JsonStore<TestModel> store = kind switch
        {
            "separate" => new JsonSeparateStore<TestModel>(),
            "separate-bulk" => new JsonSeparateBulkStore<TestModel>(),
            "batch" => new JsonBatchStore<TestModel>(),
            _ => new JsonBatchBulkStore<TestModel>(),
        };
        var name = "items";
        if (kind.StartsWith("batch"))
        {
            store.SetSettings(new BatchSettings { Location = _location, Name = name, BatchSize = 2 });
        }
        else
        {
            store.SetSettings(new Settings(_location, name));
        }
        // TASK-811: every separate and batch store keeps its files in the location (the sync ones used Location/Name/).
        return (store, store.PathDirectory!);
    }

    [Theory]
    [MemberData(nameof(DestroyKinds))]
    public void Destroy_removes_the_store_s_files_and_keeps_a_foreign_file(string kind)
    {
        var (store, directory) = SyncStore(kind);
        store.Create(new TestModel { Name = "x" });
        var foreign = System.IO.Path.Combine(directory, "keep.txt");
        File.WriteAllText(foreign, "not the store's");

        store.Invoking(s => s.Destroy()).Should().NotThrow();

        File.Exists(foreign).Should().BeTrue("Destroy deletes only the store's own files");
        Directory.GetFiles(directory).Should().Equal(new[] { foreign });
    }

    [Theory]
    [MemberData(nameof(DestroyKinds))]
    public async Task Async_destroy_removes_the_store_s_files_and_keeps_a_foreign_file(string kind)
    {
        AsyncJsonStore<TestModel> store = kind switch
        {
            "separate" => new AsyncJsonSeparateStore<TestModel>(),
            "separate-bulk" => new AsyncJsonSeparateBulkStore<TestModel>(),
            "batch" => new AsyncJsonBatchStore<TestModel>(),
            _ => new AsyncJsonBatchBulkStore<TestModel>(),
        };
        if (kind.StartsWith("batch"))
        {
            store.SetSettings(new BatchSettings { Location = _location, Name = "items", BatchSize = 2 });
        }
        else
        {
            store.SetSettings(new Settings(_location, "items"));
        }
        await store.CreateAsync(new TestModel { Name = "x" });
        var directory = store.PathDirectory!;   // the async separate stores keep their files in Location itself
        var foreign = System.IO.Path.Combine(directory, "keep.txt");
        File.WriteAllText(foreign, "not the store's");

        await store.Invoking(s => s.DestroyAsync()).Should().NotThrowAsync();

        File.Exists(foreign).Should().BeTrue();
        Directory.GetFiles(directory).Should().Equal(new[] { foreign });
    }

    [Fact]
    public void Destroy_of_a_store_alone_in_its_directory_still_removes_the_directory()
    {
        var (store, directory) = SyncStore("separate-bulk");
        store.Create(new TestModel { Name = "x" });

        store.Destroy();

        Directory.Exists(directory).Should().BeFalse("TASK-504's behaviour is kept when nothing else is there");
    }

    // ---- A failed save keeps the previous data ----

    /// <summary>A JSON serializer whose stream writes start throwing once <see cref="Fail"/> is set.</summary>
    private sealed class Flaky : ISerializer
    {
        private readonly ISerializer _inner = new SystemJsonSerializer();
        public bool Fail { get; set; }
        private void Check() { if (Fail) throw new InvalidOperationException("boom"); }

        public void Serialize(Stream stream, object value) { Check(); _inner.Serialize(stream, value); }
        public void Serialize<T>(Stream stream, T value) { Check(); _inner.Serialize(stream, value); }
        public Task SerializeAsync(Stream stream, object value, CancellationToken ct = default) { Check(); return _inner.SerializeAsync(stream, value, ct); }
        public Task SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default) { Check(); return _inner.SerializeAsync(stream, value, ct); }
        public string ContentType => _inner.ContentType;
        public SerializationFormat Format => _inner.Format;
        public string Serialize(object value) => _inner.Serialize(value);
        public string Serialize<T>(T value) => _inner.Serialize(value);
        public object? Deserialize(string data, Type type) => _inner.Deserialize(data, type);
        public T? Deserialize<T>(string data) => _inner.Deserialize<T>(data);
        public byte[] SerializeToBytes(object value) => _inner.SerializeToBytes(value);
        public byte[] SerializeToBytes<T>(T value) => _inner.SerializeToBytes(value);
        public object? DeserializeFromBytes(byte[] data, Type type) => _inner.DeserializeFromBytes(data, type);
        public T? DeserializeFromBytes<T>(byte[] data) => _inner.DeserializeFromBytes<T>(data);
        public object? Deserialize(Stream stream, Type type) => _inner.Deserialize(stream, type);
        public T? Deserialize<T>(Stream stream) => _inner.Deserialize<T>(stream);
        public Task<object?> DeserializeAsync(Stream stream, Type type, CancellationToken ct = default) => _inner.DeserializeAsync(stream, type, ct);
        public Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default) => _inner.DeserializeAsync<T>(stream, ct);
    }

    private sealed class FlakyJsonStore : JsonStore<TestModel> { public FlakyJsonStore(Flaky s) { _serializer = s; } }
    private sealed class FlakySeparateStore : JsonSeparateStore<TestModel> { public FlakySeparateStore(Flaky s) { _serializer = s; } }
    private sealed class FlakyBatchStore : JsonBatchStore<TestModel> { public FlakyBatchStore(Flaky s) { _serializer = s; } }
    private sealed class FlakyAsyncJsonStore : AsyncJsonStore<TestModel> { public FlakyAsyncJsonStore(Flaky s) { _serializer = s; } }
    private sealed class FlakyAsyncSeparateStore : AsyncJsonSeparateStore<TestModel> { public FlakyAsyncSeparateStore(Flaky s) { _serializer = s; } }
    private sealed class FlakyAsyncBatchStore : AsyncJsonBatchStore<TestModel> { public FlakyAsyncBatchStore(Flaky s) { _serializer = s; } }

    private static string[] Snapshot(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(f => f)
            .Select(f => f + "=" + File.ReadAllText(f)).ToArray();

    [Theory]
    [InlineData("single")]
    [InlineData("separate")]
    [InlineData("batch")]
    public void A_failed_save_leaves_the_previous_files_as_they_were(string kind)
    {
        var flaky = new Flaky();
        JsonStore<TestModel> store = kind switch
        {
            "single" => new FlakyJsonStore(flaky),
            "separate" => new FlakySeparateStore(flaky),
            _ => new FlakyBatchStore(flaky),
        };
        store.SetSettings(kind == "batch" ? new BatchSettings { Location = _location, Name = "items", BatchSize = 10 } : new Settings(_location, "items"));
        var id = store.Create(new TestModel { Name = "first" });
        var before = Snapshot(Dir);

        flaky.Fail = true;
        var first = store.Read(id)!;
        first.Name = "changed";
        store.Invoking(s => s.Update(first)).Should().Throw<InvalidOperationException>();

        Snapshot(Dir).Should().Equal(before, "the previous file survives a failed write, and no temp file is left");
    }

    [Theory]
    [InlineData("single")]
    [InlineData("separate")]
    [InlineData("batch")]
    public async Task An_async_failed_save_leaves_the_previous_files_as_they_were(string kind)
    {
        var flaky = new Flaky();
        AsyncJsonStore<TestModel> store = kind switch
        {
            "single" => new FlakyAsyncJsonStore(flaky),
            "separate" => new FlakyAsyncSeparateStore(flaky),
            _ => new FlakyAsyncBatchStore(flaky),
        };
        store.SetSettings(kind == "batch" ? new BatchSettings { Location = _location, Name = "items", BatchSize = 10 } : new Settings(_location, "items"));
        var id = await store.CreateAsync(new TestModel { Name = "first" });
        var before = Snapshot(Dir);

        flaky.Fail = true;
        var first = (await store.ReadAsync(id))!;
        first.Name = "changed";
        await store.Invoking(s => s.UpdateAsync(first)).Should().ThrowAsync<InvalidOperationException>();

        Snapshot(Dir).Should().Equal(before);
    }

    // ---- A null record is skipped ----

    [Fact]
    public async Task The_async_loader_skips_a_null_record()
    {
        var store = new AsyncJsonStore<TestModel>();
        store.SetSettings(new Settings(_location, "items.json"));
        var id = Guid.NewGuid();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.Path!)!);
        File.WriteAllText(store.Path!, $"[null, {{\"guid\":\"{id}\",\"name\":\"kept\"}}]");

        (await store.ReadAsync(id))!.Name.Should().Be("kept");
    }

    [Fact]
    public void The_sync_loader_skips_a_null_record()
    {
        var settings = new Settings(_location, "items.json");
        var probe = new JsonStore<TestModel>();
        probe.SetSettings(settings);
        var path = probe.Path!;
        var id = Guid.NewGuid();
        File.WriteAllText(path, $"[null, {{\"guid\":\"{id}\",\"name\":\"kept\"}}]");

        var store = new JsonStore<TestModel>();
        store.SetSettings(settings);   // the sync store loads here

        store.Read(id)!.Name.Should().Be("kept");
    }

    // ---- An empty async store reads the disk once ----

    private sealed class CountingAsyncJsonStore : AsyncJsonStore<TestModel>
    {
        public int Loads { get; private set; }
        protected override async Task LoadDataAsync(CancellationToken ct) { Loads++; await base.LoadDataAsync(ct); }
    }

    [Fact]
    public async Task An_empty_async_store_reads_the_disk_once()
    {
        var store = new CountingAsyncJsonStore();
        store.SetSettings(new Settings(_location, "empty.json"));

        (await store.CountAsync()).Should().Be(0);
        (await store.CountAsync()).Should().Be(0);
        (await store.ReadAsync(x => true, null, null, null)).Should().BeEmpty();

        store.Loads.Should().Be(1);
    }

    [Fact]
    public async Task After_destroy_the_async_store_reads_the_disk_again()
    {
        var store = new CountingAsyncJsonStore();
        store.SetSettings(new Settings(_location, "empty.json"));
        await store.CountAsync();

        await store.DestroyAsync();
        await store.CountAsync();

        store.Loads.Should().Be(2);
    }
}
