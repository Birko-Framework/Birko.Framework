using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Sync.Json.Models;
using Birko.Data.Sync.Json.Stores;
using Birko.Data.Sync.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Json.Tests;

/// <summary>
/// CR-M163: first tests for Birko.Data.Sync.Json — round-trip last-sync-time Set/Get, scope isolation,
/// empty/null cases, and CreateKnowledgeItem field derivation, against a real temp-file JSON store.
/// Also exercises the CR-M162 fix (SetLastSyncTimeAsync updates every matching item in one bulk write).
/// </summary>
public class AsyncJsonSyncKnowledgeStoreTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public AsyncJsonSyncKnowledgeStoreTests()
    {
        // The JSON store resolves a RELATIVE location against the working directory (it rejects an
        // absolute path — the drive colon fails its path validator), so mirror the Birko.Data.JSON.Tests.
        _location = "birko-syncjson-" + Guid.NewGuid().ToString("N");
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private AsyncJsonSyncKnowledgeStore NewStore(string file = "sync.json")
    {
        var store = new AsyncJsonSyncKnowledgeStore();
        store.SetSettings(new Settings(_location, file));
        return store;
    }

    [Fact]
    public async Task SetLastSyncTime_UpdatesEveryMatchingItem_AndGetReturnsMax()
    {
        var store = NewStore();
        await store.CreateAsync(new JsonSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "S", LastSyncedAt = DateTime.UtcNow.AddDays(-2) });
        await store.CreateAsync(new JsonSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "S", LastSyncedAt = DateTime.UtcNow.AddDays(-1) });

        var stamp = new DateTime(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);
        var returned = await store.SetLastSyncTimeAsync("S", stamp, CancellationToken.None);

        returned.Should().Be(stamp);
        (await store.GetLastSyncTimeAsync("S", CancellationToken.None)).Should().Be(stamp);
        // CR-M162: the single bulk write must have updated BOTH items, not just one.
        var items = (await store.ReadAsync(x => x.Scope == "S", ct: CancellationToken.None)).ToList();
        items.Should().HaveCount(2);
        items.Should().OnlyContain(i => i.LastSyncedAt == stamp);
    }

    [Fact]
    public async Task SetLastSyncTime_OnlyAffectsMatchingScope()
    {
        var store = NewStore();
        await store.CreateAsync(new JsonSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "S", LastSyncedAt = DateTime.UtcNow.AddDays(-5) });
        var otherStamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await store.CreateAsync(new JsonSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "T", LastSyncedAt = otherStamp });

        await store.SetLastSyncTimeAsync("S", new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        (await store.GetLastSyncTimeAsync("T", CancellationToken.None)).Should().Be(otherStamp, "the other scope must be untouched");
    }

    [Fact]
    public async Task SetLastSyncTime_Null_ReturnsNull_AndDoesNotChange()
    {
        var store = NewStore();
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await store.CreateAsync(new JsonSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "S", LastSyncedAt = stamp });

        var returned = await store.SetLastSyncTimeAsync("S", null, CancellationToken.None);

        returned.Should().BeNull();
        (await store.GetLastSyncTimeAsync("S", CancellationToken.None)).Should().Be(stamp);
    }

    [Fact]
    public async Task GetLastSyncTime_EmptyScope_ReturnsNull()
    {
        var store = NewStore();
        (await store.GetLastSyncTimeAsync("nope", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SetLastSyncTime_EmptyScope_IsANoOp_AndGetStaysNull()
    {
        // CR-L214: last-sync-time is derived from the scope's items. Stamping a scope with no items
        // echoes the value back but persists nothing, so a subsequent Get still returns null (the
        // documented, provider-safe limitation — the provider always writes items before stamping).
        var store = NewStore();

        var stamp = new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc);
        var returned = await store.SetLastSyncTimeAsync("empty", stamp, CancellationToken.None);

        returned.Should().Be(stamp, "the method echoes the requested stamp back");
        (await store.GetLastSyncTimeAsync("empty", CancellationToken.None))
            .Should().BeNull("an empty scope has no item to carry the derived timestamp");
    }

    [Fact]
    public void Model_HasNoVestigialIdField()
    {
        // CR-L213: the dead int Id property (serialized "id") was removed; identity is covered by
        // Guid / EntityGuid. Assert it stays gone so a reintroduction is caught.
        typeof(JsonSyncKnowledgeItem)
            .GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Should().BeNull("the vestigial int Id field was removed under CR-L213");
    }

    [Fact]
    public void CreateKnowledgeItem_DerivesDeletionFlagsFromHashes()
    {
        var store = NewStore();
        var guid = Guid.NewGuid();
        var options = new SyncOptions { Scope = "S" };

        var both = store.CreateKnowledgeItem(guid, "lh", "rh", options);
        both.EntityGuid.Should().Be(guid);
        both.Scope.Should().Be("S");
        both.IsLocalDeleted.Should().BeFalse();
        both.IsRemoteDeleted.Should().BeFalse();

        store.CreateKnowledgeItem(guid, "", "rh", options).IsLocalDeleted.Should().BeTrue();
        store.CreateKnowledgeItem(guid, "lh", null, options).IsRemoteDeleted.Should().BeTrue();
    }
}
