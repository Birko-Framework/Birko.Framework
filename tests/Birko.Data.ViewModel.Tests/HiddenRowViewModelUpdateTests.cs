using System;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.Repositories;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ViewModel.Tests;

/// <summary>
/// TASK-454 — a row the soft-delete decorator hides reads as absent (<c>Read</c> answers null), so a view-model update
/// finds nothing to merge with and writes the fresh model. It used to resurrect the row with <c>DeletedAt</c> cleared
/// and every unmapped column blanked; since TASK-159 the decorator's <c>Update</c> refuses a soft-deleted row. These pin
/// that the refusal reaches the view-model caller and the stored row is left exactly as it was.
/// </summary>
public class HiddenRowViewModelUpdateTests
{
    public class Post : AbstractModel, ISoftDeletable
    {
        public string? Title { get; set; }
        public string? Body { get; set; }   // not mapped by the view model
        public DateTime? DeletedAt { get; set; }
        public Post Copy() => new() { Guid = Guid, Title = Title, Body = Body, DeletedAt = DeletedAt };
    }

    public class PostVm : ILoadable<Post>
    {
        public Guid? Guid { get; set; }
        public string? Title { get; set; }
        public void LoadFrom(Post data) { Guid = data.Guid; Title = data.Title; }
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = new(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc);
        public DateTime Now => UtcNow;
        public DateTimeOffset OffsetUtcNow => new(UtcNow);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private sealed class SyncRepo(Birko.Data.Stores.IStore<Post> store) : AbstractViewModelRepository<PostVm, Post>(store)
    {
        protected override void MapToModel(PostVm source, Post target) { target.Guid = source.Guid; target.Title = source.Title; }
    }

    private sealed class AsyncRepo(Birko.Data.Stores.IAsyncStore<Post> store) : AbstractAsyncViewModelRepository<PostVm, Post>(store)
    {
        protected override void MapToModel(PostVm source, Post target) { target.Guid = source.Guid; target.Title = source.Title; }
    }

    [Fact]
    public void Updating_a_soft_deleted_row_through_a_view_model_is_refused_and_leaves_the_row_as_it_was()
    {
        var inner = new InMemoryStore<Post>();
        var guid = inner.Create(new Post { Title = "t", Body = "kept" });
        var store = new SoftDeleteStoreWrapper<InMemoryStore<Post>, Post>(inner, new Clock());
        store.Delete(inner.Read(guid)!.Copy());
        var deletedAt = inner.Read(guid)!.DeletedAt;
        deletedAt.Should().NotBeNull("the row is soft-deleted, which is the precondition");

        var act = () => new SyncRepo(store).Update(new PostVm { Guid = guid, Title = "edited" });

        act.Should().Throw<EntityNotFoundException>("a hidden row is refused, never resurrected");
        var row = inner.Read(guid)!;
        row.DeletedAt.Should().Be(deletedAt, "still soft-deleted");
        row.Title.Should().Be("t");
        row.Body.Should().Be("kept", "the unmapped column was not blanked");
    }

    [Fact]
    public async Task Updating_a_soft_deleted_row_through_an_async_view_model_is_refused_and_leaves_the_row_as_it_was()
    {
        var inner = new AsyncInMemoryStore<Post>();
        var guid = await inner.CreateAsync(new Post { Title = "t", Body = "kept" });
        var store = new AsyncSoftDeleteStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, new Clock());
        await store.DeleteAsync((await inner.ReadAsync(guid))!.Copy());
        var deletedAt = (await inner.ReadAsync(guid))!.DeletedAt;
        deletedAt.Should().NotBeNull("the row is soft-deleted, which is the precondition");

        var act = () => new AsyncRepo(store).UpdateAsync(new PostVm { Guid = guid, Title = "edited" });

        await act.Should().ThrowAsync<EntityNotFoundException>("a hidden row is refused, never resurrected");
        var row = (await inner.ReadAsync(guid))!;
        row.DeletedAt.Should().Be(deletedAt, "still soft-deleted");
        row.Title.Should().Be("t");
        row.Body.Should().Be("kept", "the unmapped column was not blanked");
    }
}
