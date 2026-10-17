using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Time;
using FluentAssertions;
using Xunit;
using Post = Birko.Data.Patterns.Tests.PropertyUpdateIncrementDecoratorTests.Post;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// TASK-638: a soft delete retires a default (IsDefault false, marker null) on every delete path, and the slug probe asks
/// beneath SoftDelete, so a soft-deleted row keeps its slug. Measured on what the inner store holds afterwards. The
/// refusal itself needs a unique index: see the live twin in Birko.Data.Migrations.SQL.Tests.
/// </summary>
public class Task638SoftDeletedUniqueTests
{
    public class Choice : AbstractModel, IDefaultMarker, ISoftDeletable
    {
        public string? Name { get; set; }
        public bool IsDefault { get; set; }
        public bool? DefaultMarker { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    private static readonly IDateTimeProvider Clock = new SystemDateTimeProvider();

    private static Choice Default(string name) => new() { Name = name, IsDefault = true, DefaultMarker = true };

    private static void ShouldBeRetired(Choice stored)
    {
        stored.DeletedAt.Should().NotBeNull();
        stored.IsDefault.Should().BeFalse("a deleted row is invisible, so it cannot be the default");
        stored.DefaultMarker.Should().BeNull("the marker would hold the unique index against every later promotion");
    }

    [Fact]
    public void Sync_single_delete_retires_the_default()
    {
        var inner = new InMemoryStore<Choice>();
        var item = Default("a");
        inner.Create(item);

        new SoftDeleteStoreWrapper<InMemoryStore<Choice>, Choice>(inner, Clock).Delete(item);

        ShouldBeRetired(inner.Read(item.Guid!.Value)!);
    }

    [Fact]
    public void Sync_batch_and_filter_deletes_retire_the_default()
    {
        var inner = new InMemoryStore<Choice>();
        var a = Default("a");
        var b = Default("b");
        inner.Create(new[] { a, b });
        var store = new SoftDeleteBulkStoreWrapper<InMemoryStore<Choice>, Choice>(inner, Clock);

        store.Delete(new[] { a });
        store.Delete(x => x.Name == "b");

        ShouldBeRetired(inner.Read(a.Guid!.Value)!);
        ShouldBeRetired(inner.Read(b.Guid!.Value)!);
    }

    [Fact]
    public async Task Async_single_batch_and_filter_deletes_retire_the_default()
    {
        var inner = new AsyncInMemoryStore<Choice>();
        var a = Default("a");
        var b = Default("b");
        var c = Default("c");
        await inner.CreateAsync(new[] { a, b, c });
        var store = new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Choice>, Choice>(inner, Clock);

        await store.DeleteAsync(a);
        await store.DeleteAsync(new[] { b });
        await store.DeleteAsync(x => x.Name == "c");

        foreach (var guid in new[] { a.Guid, b.Guid, c.Guid })
            ShouldBeRetired((await inner.ReadAsync(guid!.Value))!);
    }

    [Fact]
    public async Task Filter_delete_of_a_model_without_a_default_sets_only_DeletedAt()
    {
        var inner = new AsyncInMemoryStore<Post>();
        await inner.CreateAsync(new Post { Title = "a", Slug = "a" });

        await new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, Clock).DeleteAsync(x => x.Title == "a");

        var stored = (await inner.ReadAsync(null, null, null, null)).Single();
        stored.DeletedAt.Should().NotBeNull();
        stored.Slug.Should().Be("a");
    }

    [Fact]
    public void Sync_slug_of_a_soft_deleted_row_stays_reserved()
    {
        var inner = new InMemoryStore<Post>();
        var store = new SluggableBulkStoreWrapper<SoftDeleteBulkStoreWrapper<InMemoryStore<Post>, Post>, Post>(
            new SoftDeleteBulkStoreWrapper<InMemoryStore<Post>, Post>(inner, Clock));
        var old = new Post { Title = "Widget" };
        store.Create(old);
        store.Delete(old);

        var fresh = new Post { Title = "Widget" };
        store.Create(fresh);

        fresh.Slug.Should().Be("widget-2");
        inner.Read(old.Guid!.Value)!.Slug.Should().Be("widget", "the deleted row keeps its slug");
        store.Read(x => x.Title == "Widget", null, null, null).Should().ContainSingle("reads still hide the deleted row");
    }

    [Fact]
    public async Task Async_slug_of_a_soft_deleted_row_stays_reserved_on_create_and_update()
    {
        var inner = new AsyncInMemoryStore<Post>();
        var store = new AsyncSluggableBulkStoreWrapper<AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>, Post>(
            new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, Clock));
        var old = new Post { Title = "Widget" };
        await store.CreateAsync(old);
        var other = new Post { Title = "Gadget" };
        await store.CreateAsync(other);
        await store.DeleteAsync(old);

        var fresh = new Post { Title = "Widget" };
        await store.CreateAsync(fresh);
        other.Slug = null;
        other.Title = "Widget";
        await store.UpdateAsync(other);

        fresh.Slug.Should().Be("widget-2");
        other.Slug.Should().Be("widget-3");
    }
}
