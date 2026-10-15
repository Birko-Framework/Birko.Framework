using System;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Concurrency;
using Birko.Data.Patterns.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// CR-M125: the versioned wrapper's UpdateAsync only checked the version when the read returned a row;
/// a null read skipped the check and the write proceeded with Version++ — a silent lost-update. A
/// missing row is now treated as a conflict. (Best-effort read-check-write; true locking needs the
/// inner store's update predicate.)
/// </summary>
public class VersionedStoreWrapperTests
{
    private class Doc : AbstractModel, IVersioned
    {
        public long Version { get; set; }
        public string? Name { get; set; }
    }

    private static AsyncVersionedStoreWrapper<Doc> NewAsync(out AsyncInMemoryStore<Doc> inner)
    {
        inner = new AsyncInMemoryStore<Doc>();
        return new AsyncVersionedStoreWrapper<Doc>(inner);
    }

    [Fact]
    public async Task CreateAsync_sets_version_to_one()
    {
        var wrapper = NewAsync(out _);
        var doc = new Doc { Name = "a" };

        await wrapper.CreateAsync(doc);

        doc.Version.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_with_matching_version_increments_and_persists()
    {
        var wrapper = NewAsync(out var inner);
        var doc = new Doc { Name = "a" };
        await wrapper.CreateAsync(doc); // Version = 1

        await wrapper.UpdateAsync(doc); // matches stored Version 1

        doc.Version.Should().Be(2);
        (await inner.ReadAsync(doc.Guid!.Value))!.Version.Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_with_stale_version_throws_conflict()
    {
        var wrapper = NewAsync(out _);
        var doc = new Doc { Name = "a" };
        await wrapper.CreateAsync(doc); // stored Version = 1

        var stale = new Doc { Guid = doc.Guid, Name = "b", Version = 0 }; // mismatched version

        Func<Task> act = () => wrapper.UpdateAsync(stale);

        await act.Should().ThrowAsync<ConcurrentUpdateException>();
    }

    [Fact]
    public async Task UpdateAsync_on_a_missing_row_is_not_found_not_silent_pass()
    {
        var wrapper = NewAsync(out _);
        var neverCreated = new Doc { Guid = Guid.NewGuid(), Name = "ghost", Version = 5 };

        Func<Task> act = () => wrapper.UpdateAsync(neverCreated);

        // SH-M435 (TASK-159): still not a silent pass (CR-M125), but NOT FOUND, the Update contract StoreSave relies on.
        await act.Should().ThrowAsync<Birko.Data.Exceptions.EntityNotFoundException>("CR-M125: a missing row must not silently pass");
    }

    [Fact]
    public void Sync_Update_on_a_missing_row_is_not_found()
    {
        var inner = new InMemoryStore<Doc>();
        var wrapper = new VersionedStoreWrapper<Doc>(inner);
        var neverCreated = new Doc { Guid = Guid.NewGuid(), Name = "ghost", Version = 5 };

        Action act = () => wrapper.Update(neverCreated);

        act.Should().Throw<Birko.Data.Exceptions.EntityNotFoundException>();
    }

    /// <summary>SH-M435 (TASK-159): so Save of an unstored Guid creates it, as on every other store (TASK-555).</summary>
    [Fact]
    public async Task SaveAsync_of_an_unstored_guid_creates_it()
    {
        var wrapper = NewAsync(out var inner);
        var doc = new Doc { Guid = Guid.NewGuid(), Name = "imported" };

        await wrapper.SaveAsync(doc);

        (await inner.ReadAsync(doc.Guid!.Value))!.Version.Should().Be(1);
    }

    private sealed class FailingStore : InMemoryStore<Doc>
    {
        public bool Fail { get; set; }

        protected override void UpdateCore(Doc data, Birko.Data.Stores.StoreDataDelegate<Doc>? storeDelegate = null)
        {
            if (Fail) throw new System.IO.IOException("transient");
            base.UpdateCore(data, storeDelegate);
        }
    }

    /// <summary>SH-M335 (TASK-159): a failed write leaves the entity at the stored version, so a retry succeeds.</summary>
    [Fact]
    public void A_failed_update_can_be_retried()
    {
        var inner = new FailingStore();
        var wrapper = new VersionedStoreWrapper<Doc>(inner);
        var doc = new Doc { Name = "a" };
        wrapper.Create(doc);
        doc.Name = "b";

        inner.Fail = true;
        var failing = () => wrapper.Update(doc);
        failing.Should().Throw<System.IO.IOException>();
        doc.Version.Should().Be(1);

        inner.Fail = false;
        wrapper.Update(doc);
        inner.Read(doc.Guid!.Value)!.Version.Should().Be(2);
    }
}
