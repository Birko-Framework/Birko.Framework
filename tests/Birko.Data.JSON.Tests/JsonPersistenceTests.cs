using Birko.Configuration;
using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Birko.Data.JSON.Tests;

/// <summary>
/// Persistence round-trip regression tests for the sync JSON stores.
/// Guards CODE-REVIEW-AUDIT findings CR-C07 (single-file store wrote a Dictionary but read a List)
/// and CR-C08 (separate store never persisted newly-created entities and threw on first save).
/// </summary>
/// <remarks>
/// PathValidator.ValidateDirectory rejects any path containing directory separators or a drive
/// colon, so the store's Location must be a single relative segment (resolved against the current
/// working directory). Tests create that directory up front and clean it up.
/// </remarks>
public class JsonPersistenceTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public JsonPersistenceTests()
    {
        _location = "birko-json-tests-" + Guid.NewGuid().ToString("N");
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch { /* best-effort cleanup */ }
    }

    // CR-C07: JsonStore.SaveData wrote the whole Dictionary<Guid,T> (a JSON object) while LoadData
    // deserializes List<T> (a JSON array), so a saved single-file store could never be reloaded.
    [Fact]
    public void JsonStore_CreateThenReloadInNewInstance_RoundTrips()
    {
        var settings = new Settings(_location, "single-store.json");

        var writer = new Birko.Data.JSON.Stores.JsonStore<TestModel>();
        writer.SetSettings(settings);
        writer.Create(new TestModel { Name = "alpha", Value = 42 });

        // A fresh instance forces a load from disk (SetSettings -> LoadData).
        var reader = new Birko.Data.JSON.Stores.JsonStore<TestModel>();
        reader.SetSettings(settings);

        var items = reader.Read().ToList();
        items.Should().ContainSingle();
        items[0].Name.Should().Be("alpha");
        items[0].Value.Should().Be(42);
    }

    // CR-C08: JsonSeparateStore.SaveData guarded the write with `if (_files.ContainsKey(key))` — false
    // for a brand-new entity — then unconditionally read `_files[key]`, throwing KeyNotFoundException
    // on the very first save and never writing the file.
    [Fact]
    public void JsonSeparateStore_CreateNewEntity_WritesFileWithoutThrowing()
    {
        var settings = new Settings(_location, "sep");

        var store = new Birko.Data.JSON.Stores.JsonSeparateStore<TestModel>();
        store.SetSettings(settings);

        Action create = () => store.Create(new TestModel { Name = "beta", Value = 7 });
        create.Should().NotThrow();

        // The per-entity file must actually be on disk (files are named "{Name}-{guid}").
        var dir = store.GetPath()!;
        Directory.Exists(dir).Should().BeTrue();
        Directory.GetFiles(dir, "sep-*").Should().ContainSingle();
    }
}
