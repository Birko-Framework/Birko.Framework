using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.XML.Stores;
using Birko.Serialization;
using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.XML.Tests;

/// <summary>
/// CR-M182: EnsureDataLoadedAsync gated loading on `_items.Count == 0`, so a store holding zero rows
/// re-read the disk on every read/count/aggregate call. It now tracks an explicit `_loaded` flag.
/// CR-M183: SaveData(Async) did File.Delete then OpenWrite/FileMode.Create, so a failure mid-write
/// destroyed the existing file (data loss). It now writes to a temp file then atomically replaces.
/// </summary>
public class XmlLoadFlagAndAtomicSaveTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public XmlLoadFlagAndAtomicSaveTests()
    {
        _location = "birko-xml-flag-tests-" + Guid.NewGuid().ToString("N");
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }

    // ---- CR-M182 ----

    private sealed class CountingXmlStore : AsyncXmlStore<TestModel>
    {
        public int LoadCalls { get; private set; }
        protected override async Task LoadDataAsync(CancellationToken ct)
        {
            LoadCalls++;
            await base.LoadDataAsync(ct);
        }
    }

    [Fact]
    public async Task EmptyStore_LoadsFromDiskOnlyOnce()
    {
        var store = new CountingXmlStore();
        store.SetSettings(new Settings(_location, "m182"));

        // Several operations against a legitimately-empty store.
        (await store.CountAsync()).Should().Be(0);
        (await store.CountAsync()).Should().Be(0);
        (await store.ReadAsync(x => true, null, null, null)).Should().BeEmpty();

        store.LoadCalls.Should().Be(1, "an empty store must not re-read the disk on every call (CR-M182)");
    }

    [Fact]
    public async Task Destroy_ForcesReloadOnNextAccess()
    {
        var store = new CountingXmlStore();
        store.SetSettings(new Settings(_location, "m182b"));

        await store.CreateAsync(new TestModel { Name = "a" });
        store.LoadCalls.Should().Be(1);

        await store.DestroyAsync();
        (await store.CountAsync()).Should().Be(0);

        store.LoadCalls.Should().Be(2, "destroy resets the loaded flag so the next access reloads (CR-M182)");
    }

    // ---- CR-M183 ----

    /// <summary>Delegates to a real XML serializer but throws on the Nth stream serialize.</summary>
    private sealed class FlakyWriteSerializer : ISerializer
    {
        private readonly ISerializer _inner;
        private readonly int _throwOnCall;
        private int _calls;
        public FlakyWriteSerializer(ISerializer inner, int throwOnCall) { _inner = inner; _throwOnCall = throwOnCall; }

        public Task SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
        {
            if (++_calls == _throwOnCall) throw new InvalidOperationException("boom");
            return _inner.SerializeAsync(stream, value, cancellationToken);
        }
        public Task SerializeAsync(Stream stream, object value, CancellationToken cancellationToken = default)
            => _inner.SerializeAsync(stream, value, cancellationToken);

        // Everything else delegates straight through.
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
        public void Serialize(Stream stream, object value) => _inner.Serialize(stream, value);
        public void Serialize<T>(Stream stream, T value) => _inner.Serialize(stream, value);
        public object? Deserialize(Stream stream, Type type) => _inner.Deserialize(stream, type);
        public T? Deserialize<T>(Stream stream) => _inner.Deserialize<T>(stream);
        public Task<object?> DeserializeAsync(Stream stream, Type type, CancellationToken cancellationToken = default)
            => _inner.DeserializeAsync(stream, type, cancellationToken);
        public Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
            => _inner.DeserializeAsync<T>(stream, cancellationToken);
    }

    private sealed class FlakyXmlStore : AsyncXmlStore<TestModel>
    {
        public FlakyXmlStore(ISerializer serializer) { _serializer = serializer; }
    }

    [Fact]
    public async Task FailedSave_LeavesPreviousFileIntact()
    {
        var settings = new Settings(_location, "m183");
        var realSerializer = new Birko.Serialization.Xml.SystemXmlSerializer(
            new System.Xml.XmlWriterSettings { Indent = true, OmitXmlDeclaration = false });

        // First create succeeds (call #1). The write on the SECOND create throws (call #2).
        var store = new FlakyXmlStore(new FlakyWriteSerializer(realSerializer, throwOnCall: 2));
        store.SetSettings(settings);

        await store.CreateAsync(new TestModel { Name = "first" });
        var path = store.Path!;
        var savedContent = await File.ReadAllTextAsync(path);
        savedContent.Should().Contain("first");

        // Second create's save fails — the original file must survive unchanged, not be deleted/partial.
        await store.Invoking(s => s.CreateAsync(new TestModel { Name = "second" }))
            .Should().ThrowAsync<InvalidOperationException>();

        File.Exists(path).Should().BeTrue("the previous data file must survive a failed write (CR-M183)");
        (await File.ReadAllTextAsync(path)).Should().Be(savedContent, "the file is unchanged after the failed write");
        File.Exists(path + ".tmp").Should().BeFalse("the temp file is cleaned up on failure");
    }

    [Fact]
    public async Task SuccessfulSave_LeavesNoTempFile()
    {
        var store = new AsyncXmlStore<TestModel>();
        store.SetSettings(new Settings(_location, "m183ok"));

        await store.CreateAsync(new TestModel { Name = "x" });

        File.Exists(store.Path + ".tmp").Should().BeFalse();
        var reader = new AsyncXmlStore<TestModel>();
        reader.SetSettings(new Settings(_location, "m183ok"));
        (await reader.ReadAsync(x => true, null, null, null)).Should().ContainSingle();
    }
}
