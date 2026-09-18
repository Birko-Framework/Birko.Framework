using Birko.Configuration;
using Birko.Data.JSON.Stores;
using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.JSON.Tests;

/// <summary>
/// Regressions for STORY-027 JSON low findings:
/// L129 (async bulk UpdateCoreAsync must not upsert a non-existent item),
/// L130 (AsyncJsonBatchStore.SetSettings throws a clear InvalidDataException on a non-BatchSettings),
/// L131 (LoadData tolerates a record with a null guid instead of NRE-ing).
/// </summary>
public class JsonStoreLowFindingsTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public JsonStoreLowFindingsTests()
    {
        _location = "birko-json-low-tests-" + Guid.NewGuid().ToString("N");
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }

    // ---- L129 ---------------------------------------------------------------

    [Fact]
    public async Task Async_bulk_update_does_not_insert_a_nonexistent_item()
    {
        var store = new AsyncJsonStore<TestModel>();
        store.SetSettings(new Settings(_location, "l129.json"));

        var existing = new TestModel { Guid = Guid.NewGuid(), Name = "keep" };
        await store.CreateAsync(existing);

        // Bulk-update a mix: one existing, one that was never created.
        var ghost = new TestModel { Guid = Guid.NewGuid(), Name = "ghost" };
        await store.UpdateAsync(new[]
        {
            new TestModel { Guid = existing.Guid, Name = "updated" },
            ghost,
        });

        var reader = new AsyncJsonStore<TestModel>();
        reader.SetSettings(new Settings(_location, "l129.json"));
        // Bulk read overload (filter/orderBy/limit/offset) — returns the whole collection.
        var items = (await reader.ReadAsync(null, null, null, null)).ToList();

        items.Should().ContainSingle("the non-existent item must not be upserted by a bulk Update");
        items[0].Guid.Should().Be(existing.Guid);
        items[0].Name.Should().Be("updated");
    }

    // ---- L130 ---------------------------------------------------------------

    [Fact]
    public void Async_batch_store_SetSettings_throws_clear_error_on_non_batch_settings()
    {
        var store = new AsyncJsonBatchStore<TestModel>();

        Action act = () => store.SetSettings(new Settings(_location, "l130.json"));

        act.Should().Throw<InvalidDataException>("a plain Settings must not surface an opaque InvalidCastException");
    }

    [Fact]
    public void Async_batch_store_SetSettings_accepts_batch_settings()
    {
        var store = new AsyncJsonBatchStore<TestModel>();

        Action act = () => store.SetSettings(new BatchSettings { Location = _location, Name = "l130-ok.json", BatchSize = 2 });

        act.Should().NotThrow();
    }

    // ---- L131 ---------------------------------------------------------------

    [Fact]
    public void Sync_LoadData_skips_a_record_with_a_null_guid_instead_of_throwing()
    {
        // First create one good record through the store so the file lands at the store's resolved path.
        var good = Guid.NewGuid();
        var writer = new JsonStore<TestModel>();
        writer.SetSettings(new Settings(_location, "l131.json"));
        writer.Create(new TestModel { Guid = good, Name = "good", Value = 1 });

        var path = writer.GetPath();
        path.Should().NotBeNull();

        // Inject a second record with a null Guid into the on-disk file (camelCase — the store's serializer).
        File.WriteAllText(path!,
            "[" +
            $"{{\"guid\":\"{good}\",\"name\":\"good\",\"value\":1}}," +
            "{\"guid\":null,\"name\":\"bad\",\"value\":2}" +
            "]");

        // A fresh instance triggers LoadData; the null-guid record used to NRE on item.Guid!.Value.
        var reader = new JsonStore<TestModel>();
        Action act = () => reader.SetSettings(new Settings(_location, "l131.json"));
        act.Should().NotThrow();

        var items = reader.Read().ToList();
        items.Should().ContainSingle("the null-guid record is skipped, the valid one is loaded");
        items[0].Guid.Should().Be(good);
    }
}
