using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Patterns.Decorators;
using Birko.Time;
using FluentAssertions;
using Xunit;
using Post = Birko.Data.Patterns.Tests.PropertyUpdateIncrementDecoratorTests.Post;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// TASK-649 (user's decision 2026-10-17): Save of a soft-deleted entity refuses with <see cref="EntitySoftDeletedException"/>,
/// and <c>Restore</c> brings it back explicitly. It used to fall through to a create of the same Guid, which the store
/// refused as a duplicate ("use Update or Save"), measured on InMemory, SQLite and PostgreSQL.
/// </summary>
public class Task649SoftDeletedSaveTests
{
    private static readonly IDateTimeProvider Clock = new SystemDateTimeProvider();

    [Fact]
    public void Sync_Save_of_a_soft_deleted_entity_refuses_and_names_it()
    {
        var inner = new InMemoryStore<Post>();
        var store = new SoftDeleteBulkStoreWrapper<InMemoryStore<Post>, Post>(inner, Clock);
        var post = new Post { Title = "a" };
        store.Create(post);
        store.Delete(post);
        post.Title = "b";

        var act = () => store.Save(post);

        act.Should().Throw<EntitySoftDeletedException>().Which.Guid.Should().Be(post.Guid!.Value);
        inner.Read(post.Guid!.Value)!.Should().Match<Post>(p => p.Title == "a" && p.DeletedAt != null, "nothing was written");
    }

    [Fact]
    public async Task Async_Save_of_a_soft_deleted_entity_refuses()
    {
        var inner = new AsyncInMemoryStore<Post>();
        var store = new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, Clock);
        var post = new Post { Title = "a" };
        await store.CreateAsync(post);
        await store.DeleteAsync(post);

        var act = () => store.SaveAsync(post);

        await act.Should().ThrowAsync<EntitySoftDeletedException>();
    }

    [Fact]
    public async Task Save_still_creates_a_new_entity_and_updates_a_live_one()
    {
        var inner = new AsyncInMemoryStore<Post>();
        var store = new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, Clock);
        var fresh = new Post { Guid = Guid.NewGuid(), Title = "new" };

        (await store.SaveAsync(fresh)).Should().Be(fresh.Guid!.Value);
        fresh.Title = "changed";
        await store.SaveAsync(fresh);

        (await inner.ReadAsync(fresh.Guid.Value))!.Title.Should().Be("changed");
    }

    [Fact]
    public void Sync_Restore_brings_back_the_entity_with_its_new_values()
    {
        var inner = new InMemoryStore<Post>();
        var store = new SoftDeleteBulkStoreWrapper<InMemoryStore<Post>, Post>(inner, Clock);
        var post = new Post { Title = "a" };
        store.Create(post);
        store.Delete(post);
        post.Title = "b";

        store.Restore(post);

        store.Read(post.Guid!.Value)!.Title.Should().Be("b");
        post.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Async_Restore_brings_it_back_and_a_Save_then_updates()
    {
        var inner = new AsyncInMemoryStore<Post>();
        var store = new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, Clock);
        var post = new Post { Title = "a" };
        await store.CreateAsync(post);
        await store.DeleteAsync(post);

        await store.RestoreAsync(post);
        post.Title = "c";
        await store.SaveAsync(post);

        (await store.ReadAsync(post.Guid!.Value))!.Title.Should().Be("c");
    }

    [Fact]
    public async Task Restore_of_an_entity_that_is_not_stored_is_not_found()
    {
        var store = new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(new AsyncInMemoryStore<Post>(), Clock);
        var act = () => store.RestoreAsync(new Post { Guid = Guid.NewGuid(), Title = "x" });

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task Restore_of_a_live_entity_just_writes_it()
    {
        var inner = new AsyncInMemoryStore<Post>();
        var store = new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, Clock);
        var post = new Post { Title = "a" };
        await store.CreateAsync(post);
        post.Title = "b";

        await store.RestoreAsync(post);

        (await store.ReadAsync(post.Guid!.Value))!.Title.Should().Be("b");
    }

    [Fact]
    public async Task A_bulk_update_names_soft_deleted_and_missing_items_together()
    {
        var inner = new AsyncInMemoryStore<Post>();
        var store = new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, Clock);
        var deleted = new Post { Title = "d" };
        await store.CreateAsync(deleted);
        await store.DeleteAsync(deleted);
        var missing = new Post { Guid = Guid.NewGuid(), Title = "m" };

        var act = () => store.UpdateAsync(new[] { deleted, missing });

        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Guids.Should().BeEquivalentTo(new[] { deleted.Guid!.Value, missing.Guid!.Value });
    }
}
