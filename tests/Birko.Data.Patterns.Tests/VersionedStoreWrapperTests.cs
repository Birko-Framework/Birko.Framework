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
    public async Task UpdateAsync_on_a_missing_row_throws_conflict_not_silent_pass()
    {
        var wrapper = NewAsync(out _);
        var neverCreated = new Doc { Guid = Guid.NewGuid(), Name = "ghost", Version = 5 };

        Func<Task> act = () => wrapper.UpdateAsync(neverCreated);

        await act.Should().ThrowAsync<ConcurrentUpdateException>("CR-M125: a missing row must not silently pass");
    }

    [Fact]
    public void Sync_Update_on_a_missing_row_throws_conflict()
    {
        var inner = new InMemoryStore<Doc>();
        var wrapper = new VersionedStoreWrapper<Doc>(inner);
        var neverCreated = new Doc { Guid = Guid.NewGuid(), Name = "ghost", Version = 5 };

        Action act = () => wrapper.Update(neverCreated);

        act.Should().Throw<ConcurrentUpdateException>();
    }
}
