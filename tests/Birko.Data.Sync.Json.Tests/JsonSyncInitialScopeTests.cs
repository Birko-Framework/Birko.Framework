using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.JSON.Stores;
using Birko.Data.Models;
using Birko.Data.Sync.Json.Models;
using Birko.Data.Sync.Json.Stores;
using Birko.Data.Sync.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Json.Tests;

/// <summary>
/// SH-M067 (TASK-157), on a real knowledge store whose last-sync time is derived from its rows. A first run that writes
/// no knowledge row left the scope "never synced", so every later run was an initial sync, which only downloads: the
/// local items were never uploaded.
/// </summary>
public class JsonSyncInitialScopeTests : IDisposable
{
    private readonly string _location = "birko-syncjson-" + Guid.NewGuid().ToString("N");

    public JsonSyncInitialScopeTests() => Directory.CreateDirectory(Path.GetFullPath(_location));

    public void Dispose()
    {
        try { Directory.Delete(Path.GetFullPath(_location), recursive: true); } catch { }
    }

    public class Note : AbstractModel
    {
        public string? Text { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private AsyncJsonStore<Note> Store(string file)
    {
        var store = new AsyncJsonStore<Note>();
        store.SetSettings(new Settings(_location, file));
        return store;
    }

    [Fact]
    public async Task A_scope_whose_first_run_wrote_no_knowledge_is_not_initial_afterwards()
    {
        var local = Store("local.json");
        var remote = Store("remote.json");
        var knowledge = new AsyncJsonSyncKnowledgeStore();
        knowledge.SetSettings(new Settings(_location, "knowledge.json"));
        var note = new Note { Guid = Guid.NewGuid(), Text = "written offline", UpdatedAt = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc) };
        await local.CreateAsync(note);
        var provider = new AsyncSyncProvider<AsyncJsonStore<Note>, Note, JsonSyncKnowledgeItem>(local, remote, knowledge);

        var first = await provider.SyncAsync(new SyncOptions { Scope = "notes" });
        var second = await provider.SyncAsync(new SyncOptions { Scope = "notes" });

        first.IsInitialSync.Should().BeTrue();
        second.IsInitialSync.Should().BeFalse("the first run happened");
        (await remote.ReadAsync(note.Guid!.Value)).Should().NotBeNull("the second run uploads the local item");
        (await local.ReadAsync(note.Guid!.Value)).Should().NotBeNull();
    }
}
