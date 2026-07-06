using Birko.Configuration;
using Birko.Data.XML.Stores;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.XML.Tests;

/// <summary>
/// Persistence round-trip regression tests for the XML separate/batch stores.
/// CR-C22: bulk Create/Update/Delete on the separate stores never wrote files (memory-only +
/// no-op SaveData). CR-C21: batch stores wrote a single entity per file but read List&lt;T&gt;,
/// so every entity was silently dropped on reload.
/// </summary>
/// <remarks>
/// PathValidator.ValidateDirectory rejects separators/drive colons, so Location is a single
/// relative segment resolved against the working directory.
/// </remarks>
public class XmlPersistenceTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public XmlPersistenceTests()
    {
        _location = "birko-xml-tests-" + Guid.NewGuid().ToString("N");
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }

    // CR-C22 — bulk create on the separate bulk store must persist per-entity files.
    [Fact]
    public void SeparateBulkStore_BulkCreate_PersistsAndReloads()
    {
        var settings = new Settings(_location, "sep");

        var store = new XmlSeparateBulkStore<TestModel>();
        store.SetSettings(settings);
        store.Create(new List<TestModel> { new() { Name = "a", Value = 1 }, new() { Name = "b", Value = 2 } });

        var reader = new XmlSeparateBulkStore<TestModel>();
        reader.SetSettings(settings);

        var items = reader.Read().ToList();
        items.Should().HaveCount(2);
        items.Select(x => x.Name).Should().BeEquivalentTo(new[] { "a", "b" });
    }

    // CR-C21 — batch store must write List<T> batch files that reload correctly (multiple batches).
    [Fact]
    public void BatchStore_CreateAcrossBatches_RoundTrips()
    {
        var settings = new BatchSettings { Location = _location, Name = "batch", BatchSize = 2 };

        var store = new XmlBatchStore<TestModel>();
        store.SetSettings(settings);
        store.Create(new TestModel { Name = "a", Value = 1 });
        store.Create(new TestModel { Name = "b", Value = 2 });
        store.Create(new TestModel { Name = "c", Value = 3 }); // 3 items, batch size 2 -> 2 files

        // Two batch files should exist on disk.
        Directory.GetFiles(_dir, "batch-*.xml").Should().HaveCount(2);

        var reader = new XmlBatchStore<TestModel>();
        reader.SetSettings(settings);
        var items = reader.Read().ToList();
        items.Should().HaveCount(3);
        items.Select(x => x.Name).Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    // CR-C21 — the batch *bulk* store shares the same batched persistence via inheritance.
    [Fact]
    public void BatchBulkStore_BulkCreate_RoundTrips()
    {
        var settings = new BatchSettings { Location = _location, Name = "bb", BatchSize = 2 };

        var store = new XmlBatchBulkStore<TestModel>();
        store.SetSettings(settings);
        store.Create(new List<TestModel>
        {
            new() { Name = "a" }, new() { Name = "b" }, new() { Name = "c" }, new() { Name = "d" }
        });

        var reader = new XmlBatchBulkStore<TestModel>();
        reader.SetSettings(settings);
        reader.Read().Should().HaveCount(4);
    }

    // CR-C21 (async) — async batch store round-trips.
    [Fact]
    public async Task AsyncBatchStore_CreateAcrossBatches_RoundTrips()
    {
        var settings = new BatchSettings { Location = _location, Name = "abatch", BatchSize = 2 };

        var store = new AsyncXmlBatchStore<TestModel>();
        store.SetSettings(settings);
        await store.CreateAsync(new TestModel { Name = "a" });
        await store.CreateAsync(new TestModel { Name = "b" });
        await store.CreateAsync(new TestModel { Name = "c" });

        var reader = new AsyncXmlBatchStore<TestModel>();
        reader.SetSettings(settings);
        var items = (await reader.ReadAsync(x => true, null, null, null)).ToList();
        items.Should().HaveCount(3);
    }
}
