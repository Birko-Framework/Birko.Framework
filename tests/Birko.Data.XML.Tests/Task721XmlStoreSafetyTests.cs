using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.XML.Stores;
using Birko.Serialization;
using Birko.Serialization.Xml;
using FluentAssertions;
using Xunit;

namespace Birko.Data.XML.Tests;

/// <summary>
/// TASK-721 on the XML stores, measured before the fix: the sync and async single-file and batch loaders threw
/// NullReferenceException on a nil record; the separate stores' Destroy threw IOException when anything else was in
/// the directory; a separate store's update deleted the entity's file before writing it, and a batch save deleted every
/// batch file before writing any, so a failed write lost them.
/// </summary>
public class Task721XmlStoreSafetyTests : IDisposable
{
    private readonly string _location = "birko-xml-721-" + Guid.NewGuid().ToString("N");
    private string Dir => Path.GetFullPath(_location);

    public Task721XmlStoreSafetyTests() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); } catch { }
    }

    private const string Nil = "<TestModel xsi:nil=\"true\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" />";

    /// <summary>Puts a nil record in front of the first stored record of every file the store wrote.</summary>
    private void InjectNilRecords()
    {
        foreach (var file in Directory.GetFiles(Dir, "*.xml", SearchOption.AllDirectories))
        {
            var xml = File.ReadAllText(file);
            var at = xml.IndexOf("<TestModel>", StringComparison.Ordinal);
            at.Should().BeGreaterThan(0, "the store wrote a list of records");
            File.WriteAllText(file, xml.Insert(at, Nil));
        }
    }

    // ---- a nil record is skipped ----

    [Theory]
    [InlineData("single")]
    [InlineData("batch")]
    public void The_sync_loaders_skip_a_nil_record(string kind)
    {
        Settings settings = kind == "batch" ? new BatchSettings { Location = _location, Name = "items", BatchSize = 10 } : new Settings(_location, "items");
        XmlStore<TestModel> writer = kind == "batch" ? new XmlBatchStore<TestModel>() : new XmlStore<TestModel>();
        writer.SetSettings(settings);
        var id = writer.Create(new TestModel { Name = "kept" });
        InjectNilRecords();

        XmlStore<TestModel> reader = kind == "batch" ? new XmlBatchStore<TestModel>() : new XmlStore<TestModel>();
        reader.SetSettings(settings);

        reader.Read(id)!.Name.Should().Be("kept");
    }

    [Theory]
    [InlineData("single")]
    [InlineData("batch")]
    public async Task The_async_loaders_skip_a_nil_record(string kind)
    {
        Settings settings = kind == "batch" ? new BatchSettings { Location = _location, Name = "items", BatchSize = 10 } : new Settings(_location, "items");
        AsyncXmlStore<TestModel> writer = kind == "batch" ? new AsyncXmlBatchStore<TestModel>() : new AsyncXmlStore<TestModel>();
        writer.SetSettings(settings);
        var id = await writer.CreateAsync(new TestModel { Name = "kept" });
        InjectNilRecords();

        AsyncXmlStore<TestModel> reader = kind == "batch" ? new AsyncXmlBatchStore<TestModel>() : new AsyncXmlStore<TestModel>();
        reader.SetSettings(settings);

        (await reader.ReadAsync(id))!.Name.Should().Be("kept");
    }

    // ---- Destroy keeps what is not the store's ----

    [Fact]
    public void Destroy_keeps_a_foreign_file()
    {
        var store = new XmlSeparateStore<TestModel>();
        store.SetSettings(new Settings(_location, "items"));
        store.Create(new TestModel { Name = "x" });
        var foreign = Path.Combine(store.PathDirectory!, "keep.txt");
        File.WriteAllText(foreign, "not the store's");

        store.Invoking(s => s.Destroy()).Should().NotThrow();

        Directory.GetFiles(store.PathDirectory!).Should().Equal(new[] { foreign });
    }

    [Fact]
    public async Task Async_destroy_keeps_a_foreign_file()
    {
        var store = new AsyncXmlSeparateStore<TestModel>();
        store.SetSettings(new Settings(_location, "items"));
        await store.CreateAsync(new TestModel { Name = "x" });
        var foreign = Path.Combine(store.PathDirectory!, "keep.txt");
        File.WriteAllText(foreign, "not the store's");

        await store.Invoking(s => s.DestroyAsync()).Should().NotThrowAsync();

        Directory.GetFiles(store.PathDirectory!).Should().Equal(new[] { foreign });
    }

    // ---- a failed write keeps the previous data ----

    /// <summary>An XML serializer whose Nth stream write, counted from when <see cref="FailOnWrite"/> is set, throws.</summary>
    private sealed class Flaky : ISerializer
    {
        private readonly ISerializer _inner = new SystemXmlSerializer(new System.Xml.XmlWriterSettings { Indent = true });
        private int _writes;
        private int _failOn;
        public void FailOnWrite(int n) { _writes = 0; _failOn = n; }
        private void Check() { if (_failOn > 0 && ++_writes == _failOn) throw new InvalidOperationException("boom"); }

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

    private sealed class FlakySeparate : XmlSeparateStore<TestModel> { public FlakySeparate(Flaky s) { _serializer = s; } }
    private sealed class FlakyAsyncSeparate : AsyncXmlSeparateStore<TestModel> { public FlakyAsyncSeparate(Flaky s) { _serializer = s; } }
    private sealed class FlakyBatch : XmlBatchStore<TestModel> { public FlakyBatch(Flaky s) { _serializer = s; } }
    private sealed class FlakyAsyncBatch : AsyncXmlBatchStore<TestModel> { public FlakyAsyncBatch(Flaky s) { _serializer = s; } }

    [Fact]
    public void A_failed_separate_update_keeps_the_entity_s_file()
    {
        var flaky = new Flaky();
        var store = new FlakySeparate(flaky);
        store.SetSettings(new Settings(_location, "items"));
        var id = store.Create(new TestModel { Name = "first" });

        flaky.FailOnWrite(1);
        var e = store.Read(id)!;
        e.Name = "changed";
        store.Invoking(s => s.Update(e)).Should().Throw<InvalidOperationException>();

        var reader = new XmlSeparateStore<TestModel>();
        reader.SetSettings(new Settings(_location, "items"));
        reader.Read(id)!.Name.Should().Be("first");
        Directory.GetFiles(Dir, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_async_separate_update_keeps_the_entity_s_file()
    {
        var flaky = new Flaky();
        var store = new FlakyAsyncSeparate(flaky);
        store.SetSettings(new Settings(_location, "items"));
        var id = await store.CreateAsync(new TestModel { Name = "first" });

        flaky.FailOnWrite(1);
        var e = (await store.ReadAsync(id))!;
        e.Name = "changed";
        await store.Invoking(s => s.UpdateAsync(e)).Should().ThrowAsync<InvalidOperationException>();

        var reader = new AsyncXmlSeparateStore<TestModel>();
        reader.SetSettings(new Settings(_location, "items"));
        (await reader.ReadAsync(id))!.Name.Should().Be("first");
    }

    [Fact]
    public void A_failed_batch_save_loses_no_batch()
    {
        var flaky = new Flaky();
        var settings = new BatchSettings { Location = _location, Name = "items", BatchSize = 1 };
        var store = new FlakyBatch(flaky);
        store.SetSettings(settings);
        var a = store.Create(new TestModel { Name = "a" });
        var b = store.Create(new TestModel { Name = "b" });

        flaky.FailOnWrite(2);   // the second batch of the next save
        var eb = store.Read(b)!;
        eb.Name = "b2";
        store.Invoking(s => s.Update(eb)).Should().Throw<InvalidOperationException>();

        var reader = new XmlBatchStore<TestModel>();
        reader.SetSettings(settings);
        reader.Read(a)!.Name.Should().Be("a");
        reader.Read(b)!.Name.Should().Be("b", "the batch that failed to be written keeps its previous content");
    }

    [Fact]
    public async Task A_failed_async_batch_save_loses_no_batch()
    {
        var flaky = new Flaky();
        var settings = new BatchSettings { Location = _location, Name = "items", BatchSize = 1 };
        var store = new FlakyAsyncBatch(flaky);
        store.SetSettings(settings);
        var a = await store.CreateAsync(new TestModel { Name = "a" });
        var b = await store.CreateAsync(new TestModel { Name = "b" });

        flaky.FailOnWrite(2);
        var eb = (await store.ReadAsync(b))!;
        eb.Name = "b2";
        await store.Invoking(s => s.UpdateAsync(eb)).Should().ThrowAsync<InvalidOperationException>();

        var reader = new AsyncXmlBatchStore<TestModel>();
        reader.SetSettings(settings);
        (await reader.ReadAsync(a))!.Name.Should().Be("a");
        (await reader.ReadAsync(b))!.Name.Should().Be("b");
    }
}
