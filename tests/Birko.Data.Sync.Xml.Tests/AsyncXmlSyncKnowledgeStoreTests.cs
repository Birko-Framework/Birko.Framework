using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Xml.Models;
using Birko.Data.Sync.Xml.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Xml.Tests;

/// <summary>
/// CR-M171: first tests for Birko.Data.Sync.Xml — last-sync-time Set/Get round-trip, scope isolation,
/// null/empty cases, and CreateKnowledgeItem field derivation, against a real temp-file XML store.
/// Also exercises the single-bulk-write cleanup (mirrors CR-M162 for the JSON backend).
/// </summary>
public class AsyncXmlSyncKnowledgeStoreTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public AsyncXmlSyncKnowledgeStoreTests()
    {
        // The XML store resolves a RELATIVE location against the working directory (mirrors Birko.Data.XML.Tests).
        _location = "birko-syncxml-" + Guid.NewGuid().ToString("N");
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private AsyncXmlSyncKnowledgeStore NewStore(string file = "sync")
    {
        var store = new AsyncXmlSyncKnowledgeStore();
        store.SetSettings(new Settings(_location, file));
        return store;
    }

    [Fact]
    public async Task SetLastSyncTime_UpdatesEveryMatchingItem_AndGetReturnsMax()
    {
        var store = NewStore();
        await store.CreateAsync(new XmlSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "S", LastSyncedAt = DateTime.UtcNow.AddDays(-2) });
        await store.CreateAsync(new XmlSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "S", LastSyncedAt = DateTime.UtcNow.AddDays(-1) });

        var stamp = new DateTime(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);
        var returned = await store.SetLastSyncTimeAsync("S", stamp, CancellationToken.None);

        returned.Should().Be(stamp);
        (await store.GetLastSyncTimeAsync("S", CancellationToken.None)).Should().Be(stamp);
        var items = (await store.ReadAsync(x => x.Scope == "S", ct: CancellationToken.None)).ToList();
        items.Should().HaveCount(2);
        items.Should().OnlyContain(i => i.LastSyncedAt == stamp);
    }

    [Fact]
    public async Task SetLastSyncTime_OnlyAffectsMatchingScope()
    {
        var store = NewStore();
        await store.CreateAsync(new XmlSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "S", LastSyncedAt = DateTime.UtcNow.AddDays(-5) });
        var otherStamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await store.CreateAsync(new XmlSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "T", LastSyncedAt = otherStamp });

        await store.SetLastSyncTimeAsync("S", new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        (await store.GetLastSyncTimeAsync("T", CancellationToken.None)).Should().Be(otherStamp);
    }

    [Fact]
    public async Task SetLastSyncTime_Null_ReturnsNull_AndDoesNotChange()
    {
        var store = NewStore();
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await store.CreateAsync(new XmlSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "S", LastSyncedAt = stamp });

        (await store.SetLastSyncTimeAsync("S", null, CancellationToken.None)).Should().BeNull();
        (await store.GetLastSyncTimeAsync("S", CancellationToken.None)).Should().Be(stamp);
    }

    [Fact]
    public async Task GetLastSyncTime_EmptyScope_ReturnsNull()
    {
        (await NewStore().GetLastSyncTimeAsync("nope", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void Model_HasNoVestigialIdField()
    {
        // CR-L225: the int Id (copied from SqlSyncKnowledgeItem's [IncrementField]) had no
        // auto-increment source in XML — never assigned, always serialized as 0. Identity is
        // covered by AbstractModel.Guid / EntityGuid (same removal as the JSON sibling, CR-L213).
        typeof(XmlSyncKnowledgeItem).GetProperty("Id").Should().BeNull();
    }

    [Fact]
    public async Task LoadData_LegacyFileWithIdElement_StillDeserializes()
    {
        // CR-L225 backward-compat: files written before the Id removal carry <Id>0</Id>;
        // XmlSerializer ignores unknown elements, so legacy files must still load.
        var entityGuid = Guid.NewGuid();
        var writer = NewStore("legacy");
        await writer.CreateAsync(new XmlSyncKnowledgeItem { EntityGuid = entityGuid, Scope = "S", LastSyncedAt = DateTime.UtcNow });

        var path = writer.GetPath()!;
        var xml = File.ReadAllText(path);
        xml.Should().Contain("<EntityGuid>");
        File.WriteAllText(path, xml.Replace("<EntityGuid>", "<Id>7</Id><EntityGuid>"));

        var items = (await NewStore("legacy").ReadAsync(x => x.Scope == "S", ct: CancellationToken.None)).ToList();
        items.Should().ContainSingle().Which.EntityGuid.Should().Be(entityGuid);
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
