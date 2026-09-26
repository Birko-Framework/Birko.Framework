using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// TASK-498 — the bulk decorators that touch a <see cref="PropertyUpdate{T}"/> still carry an increment through:
/// Timestamp and Audit append their own <c>Set</c> to the caller's update, SoftDelete rewrites only the filter,
/// Sluggable forwards. The increment must survive each, and the decorator's own Set must land beside it.
/// </summary>
public class PropertyUpdateIncrementDecoratorTests
{
    public class Post : AbstractModel, ITimestamped, IAuditable, ISoftDeletable, ISluggable
    {
        public int Views { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? PrevUpdatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? GetSlugSource() => Title;
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
        public DateTimeOffset OffsetUtcNow => new(Now);
        public DateOnly Today => DateOnly.FromDateTime(Now);
    }

    private sealed class FixedUser : IAuditContext
    {
        public static readonly Guid Id = Guid.NewGuid();
        public Guid? CurrentUserId => Id;
    }

    private static async Task<(AsyncInMemoryStore<Post> Store, Guid Live, Guid Deleted)> SeedAsync()
    {
        var store = new AsyncInMemoryStore<Post>();
        var live = await store.CreateAsync(new Post { Title = "live", Views = 10 });
        var deleted = await store.CreateAsync(new Post { Title = "deleted", Views = 10, DeletedAt = FixedClock.Now });
        return (store, live, deleted);
    }

    private static async Task<Post> ReadAsync(AsyncInMemoryStore<Post> store, Guid guid) => (await store.ReadAsync(guid))!;

    private static PropertyUpdate<Post> IncrementViews() => new PropertyUpdate<Post>().Increment(x => x.Views, 1);

    [Fact]
    public async Task Timestamp_Keeps_The_Increment_And_Stamps_UpdatedAt()
    {
        var (store, live, _) = await SeedAsync();

        await new AsyncTimestampBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(store, new FixedClock())
            .UpdateAsync(x => x.Title == "live", IncrementViews());

        var after = await ReadAsync(store, live);
        after.Views.Should().Be(11);
        after.UpdatedAt.Should().Be(FixedClock.Now);
    }

    [Fact]
    public async Task Audit_Keeps_The_Increment_And_Stamps_UpdatedBy()
    {
        var (store, live, _) = await SeedAsync();

        await new AsyncAuditBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(store, new FixedUser())
            .UpdateAsync(x => x.Title == "live", IncrementViews());

        var after = await ReadAsync(store, live);
        after.Views.Should().Be(11);
        after.UpdatedBy.Should().Be(FixedUser.Id);
    }

    [Fact]
    public async Task SoftDelete_Increments_Only_Live_Rows()
    {
        var (store, live, deleted) = await SeedAsync();

        await new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(store, new FixedClock())
            .UpdateAsync(x => x.Views == 10, IncrementViews());

        (await ReadAsync(store, live)).Views.Should().Be(11);
        (await ReadAsync(store, deleted)).Views.Should().Be(10, "a soft-deleted row is outside the filter");
    }

    [Fact]
    public async Task Sluggable_Forwards_The_Increment()
    {
        var (store, live, _) = await SeedAsync();

        await new AsyncSluggableBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(store)
            .UpdateAsync(x => x.Title == "live", IncrementViews());

        (await ReadAsync(store, live)).Views.Should().Be(11);
    }

    [Fact]
    public void Sync_Timestamp_Keeps_The_Increment()
    {
        var store = new InMemoryStore<Post>();
        var guid = store.Create(new Post { Title = "live", Views = 10 });

        new TimestampBulkStoreWrapper<InMemoryStore<Post>, Post>(store, new FixedClock())
            .Update(x => x.Title == "live", IncrementViews());

        var after = store.Read(guid)!;
        after.Views.Should().Be(11);
        after.UpdatedAt.Should().Be(FixedClock.Now);
    }
}
