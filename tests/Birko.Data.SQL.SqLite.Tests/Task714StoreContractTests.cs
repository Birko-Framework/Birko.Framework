using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-714 on SQLite. Measured before the fix: an update whose store delegate returned a copy with its own Guid built
/// its WHERE from the original and so rewrote the original row's key to the copy's; the sync <c>Create(null)</c> /
/// <c>Update(null)</c> threw <c>NullReferenceException</c> where the async twins returned quietly (a null create
/// answering <c>Guid.Empty</c>, a write reported that did not happen); a null element in a bulk create threw
/// <c>NullReferenceException</c> on every SQL store, where the portable stores skip it.
/// </summary>
public class Task714StoreContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t714-{Guid.NewGuid():N}");

    public Task714StoreContractTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("T714Rows")]
    public class Row : AbstractModel
    {
        [NamedField] public string? Name { get; set; }
    }

    private AsyncSQLiteStore<Row> Async()
    {
        var store = new AsyncSQLiteStore<Row>();
        store.SetSettings(new SqLiteSettings(_root, "t714a.db"));
        return store;
    }

    private SQLiteStore<Row> Sync()
    {
        var store = new SQLiteStore<Row>();
        store.SetSettings(new SqLiteSettings(_root, "t714s.db"));
        return store;
    }

    [Fact]
    public async Task An_update_whose_delegate_changes_the_key_is_refused_and_the_row_keeps_its_key()
    {
        var store = Async();
        var id = await store.CreateAsync(new Row { Name = "a" });
        var other = Guid.NewGuid();

        var act = () => store.UpdateAsync(new Row { Guid = id, Name = "b" }, _ => new Row { Guid = other, Name = "b" });

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await store.ReadAsync(id))!.Name.Should().Be("a");
        (await store.ReadAsync(other)).Should().BeNull();
    }

    [Fact]
    public void A_sync_update_whose_delegate_changes_the_key_is_refused_and_the_row_keeps_its_key()
    {
        var store = Sync();
        var id = store.Create(new Row { Name = "a" });
        var other = Guid.NewGuid();

        var act = () => store.Update(new Row { Guid = id, Name = "b" }, _ => new Row { Guid = other, Name = "b" });

        act.Should().Throw<InvalidOperationException>();
        store.Read(id)!.Name.Should().Be("a");
        store.Read(other).Should().BeNull();
    }

    [Fact]
    public async Task A_null_create_or_update_is_an_argument_error_on_both_twins()
    {
        var asyncStore = Async();
        var syncStore = Sync();

        await asyncStore.Invoking(s => s.CreateAsync((Row)null!)).Should().ThrowAsync<ArgumentNullException>();
        await asyncStore.Invoking(s => s.UpdateAsync((Row)null!)).Should().ThrowAsync<ArgumentNullException>();
        syncStore.Invoking(s => s.Create((Row)null!)).Should().Throw<ArgumentNullException>();
        syncStore.Invoking(s => s.Update((Row)null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_null_element_in_a_bulk_write_is_skipped_as_the_portable_stores_skip_it()
    {
        var asyncStore = Async();
        var syncStore = Sync();

        await asyncStore.CreateAsync(new[] { new Row { Name = "a" }, null!, new Row { Name = "b" } });
        syncStore.Create(new[] { new Row { Name = "a" }, null!, new Row { Name = "b" } });

        (await asyncStore.ReadAsync(x => true, null, null, null)).Select(r => r.Name).Should().BeEquivalentTo("a", "b");
        syncStore.Read(x => true, null, null, null).Select(r => r.Name).Should().BeEquivalentTo("a", "b");

        var stored = (await asyncStore.ReadAsync(x => true, null, null, null)).ToList();
        stored.ForEach(r => r.Name += "!");
        await asyncStore.UpdateAsync(new[] { stored[0], null!, stored[1] });
        (await asyncStore.ReadAsync(x => true, null, null, null)).Select(r => r.Name).Should().BeEquivalentTo("a!", "b!");
    }
}
