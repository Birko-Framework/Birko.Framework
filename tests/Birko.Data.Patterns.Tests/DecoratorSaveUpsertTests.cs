using System;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;
using Post = Birko.Data.Patterns.Tests.PropertyUpdateIncrementDecoratorTests.Post;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// SH-M309 (TASK-555) at the decorator layer. Every decorator re-implemented <c>Save</c> as "empty Guid → Create, else
/// Update and return the Guid", so a <c>Save</c> of an entity carrying a client-chosen Guid that is not stored wrote
/// nothing and returned the Guid — through every decorator, not just the base store. They now route through the one
/// producer, <see cref="StoreSave"/>, passing their OWN create, so the entity is created and the decorator's create
/// behaviour (here: <c>CreatedAt</c> stamped, the slug assigned) runs on it.
///
/// <para>Observed state: what the inner store holds afterwards.</para>
/// </summary>
public class DecoratorSaveUpsertTests
{
    private sealed class FixedClock : IDateTimeProvider
    {
        public static readonly DateTime Now = new(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
        public DateTimeOffset OffsetUtcNow => new(Now);
        public DateOnly Today => DateOnly.FromDateTime(Now);
    }

    private sealed class FixedUser : IAuditContext
    {
        public Guid? CurrentUserGuid { get; } = Guid.NewGuid();
    }

    public static TheoryData<string> SyncDecorators => new() { "timestamp", "audit", "soft-delete", "sluggable" };

    private static IStore<Post> Wrap(string kind, InMemoryStore<Post> inner) => kind switch
    {
        "timestamp" => new TimestampStoreWrapper<InMemoryStore<Post>, Post>(inner, new FixedClock()),
        "audit" => new AuditStoreWrapper<InMemoryStore<Post>, Post>(inner, new FixedUser()),
        "soft-delete" => new SoftDeleteStoreWrapper<InMemoryStore<Post>, Post>(inner, new FixedClock()),
        "sluggable" => new SluggableStoreWrapper<InMemoryStore<Post>, Post>(inner),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static IAsyncStore<Post> WrapAsync(string kind, AsyncInMemoryStore<Post> inner) => kind switch
    {
        "timestamp" => new AsyncTimestampStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, new FixedClock()),
        "audit" => new AsyncAuditStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, new FixedUser()),
        "soft-delete" => new AsyncSoftDeleteStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, new FixedClock()),
        "sluggable" => new AsyncSluggableStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(SyncDecorators))]
    public void Save_through_a_decorator_creates_an_entity_whose_guid_is_not_stored(string kind)
    {
        var inner = new InMemoryStore<Post>();
        var store = Wrap(kind, inner);
        var clientGuid = Guid.NewGuid();

        var returned = store.Save(new Post { Guid = clientGuid, Title = "Imported post" });

        returned.Should().Be(clientGuid);
        inner.Read(clientGuid).Should().NotBeNull($"the {kind} decorator's Save must not report a Guid it never stored");
    }

    [Theory]
    [MemberData(nameof(SyncDecorators))]
    public async Task SaveAsync_through_a_decorator_creates_an_entity_whose_guid_is_not_stored(string kind)
    {
        var inner = new AsyncInMemoryStore<Post>();
        var store = WrapAsync(kind, inner);
        var clientGuid = Guid.NewGuid();

        var returned = await store.SaveAsync(new Post { Guid = clientGuid, Title = "Imported post" });

        returned.Should().Be(clientGuid);
        (await inner.ReadAsync(clientGuid)).Should().NotBeNull($"the async {kind} decorator's SaveAsync must not report a Guid it never stored");
    }

    [Fact]
    public void The_upsert_takes_the_decorators_create_path()
    {
        var inner = new InMemoryStore<Post>();
        var store = Wrap("timestamp", inner);
        var clientGuid = Guid.NewGuid();

        store.Save(new Post { Guid = clientGuid, Title = "Imported post" });

        inner.Read(clientGuid)!.CreatedAt.Should().Be(FixedClock.Now, "the timestamp decorator stamps CreatedAt on create");
    }

    [Fact]
    public async Task The_async_upsert_takes_the_decorators_create_path()
    {
        var inner = new AsyncInMemoryStore<Post>();
        var store = WrapAsync("sluggable", inner);
        var clientGuid = Guid.NewGuid();

        await store.SaveAsync(new Post { Guid = clientGuid, Title = "Imported Post" });

        (await inner.ReadAsync(clientGuid))!.Slug.Should().Be("imported-post", "the sluggable decorator assigns a slug on create");
    }
}
