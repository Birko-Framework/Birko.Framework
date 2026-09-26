using System;
using System.IO;
using Birko.Configuration;
using Birko.Data.JSON.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.JSON.Tests;

/// <summary>
/// TASK-504: JsonSeparateBulkStore (and JsonBatchBulkStore, which inherits it) ended Destroy with
/// Directory.Delete(Path) — the store's directory joined with its file Name — instead of the directory
/// itself. AsyncJsonSeparateBulkStore deletes the directory. (JsonSeparateStore is not affected: for it
/// Location/Name is its own directory, so Path is the right thing to delete.) A consumer that destroys and re-creates a populated store every
/// run (Affiliate's compare records, Name "*.json") crashed on every run after the first.
/// PathValidator rejects absolute Windows paths, so a RELATIVE location is used.
/// </summary>
public class SeparateStoreDestroyTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public SeparateStoreDestroyTests()
    {
        _location = $"birko-json-destroy-{Guid.NewGuid():N}";
        _dir = Path.GetFullPath(_location);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("data.json")]
    [InlineData("*.json")]
    public void SeparateBulkStore_Destroy_OfPopulatedStore_DeletesItAndCanBeRecreated(string name)
    {
        var store = new JsonSeparateBulkStore<TestModel>();
        store.SetSettings(new Settings(_location, name));
        store.Create(new TestModel { Name = "x" });

        store.Invoking(x => x.Destroy()).Should().NotThrow();

        Directory.Exists(_dir).Should().BeFalse("Destroy removes the store's directory");
        AssertRecreatable(new JsonSeparateBulkStore<TestModel>(), new Settings(_location, name));
    }

    [Theory]
    [InlineData("data.json")]
    [InlineData("*.json")]
    public void BatchBulkStore_Destroy_OfPopulatedStore_DeletesItAndCanBeRecreated(string name)
    {
        var settings = new BatchSettings { Location = _location, Name = name, BatchSize = 2 };
        var store = new JsonBatchBulkStore<TestModel>();
        store.SetSettings(settings);
        store.Create(new TestModel { Name = "x" });

        store.Invoking(x => x.Destroy()).Should().NotThrow();

        Directory.Exists(_dir).Should().BeFalse("Destroy removes the store's directory");
        AssertRecreatable(new JsonBatchBulkStore<TestModel>(), settings);
    }

    private static void AssertRecreatable<TStore>(TStore store, Settings settings)
        where TStore : JsonStore<TestModel>
    {
        store.SetSettings(settings);
        var id = store.Create(new TestModel { Name = "after" });
        store.Read(id).Should().NotBeNull("a destroyed store must be usable again");
    }
}
