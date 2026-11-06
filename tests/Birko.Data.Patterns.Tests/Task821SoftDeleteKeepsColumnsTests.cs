using System;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// TASK-821 — a soft delete stamps and writes the STORED row, never the caller's instance. The decorators wrote the
/// instance they were given whole, so deleting with a partial one (what a view-model repository's <c>Delete</c> passes:
/// a fresh model carrying only what <c>MapToModel</c> assigns) blanked every column it did not carry, measured as
/// <c>Body</c> read back null. Each test deletes with a stub holding only the Guid and asserts the stored row as observed
/// state: stamped, every column intact.
/// </summary>
public class Task821SoftDeleteKeepsColumnsTests
{
    public class Note : AbstractModel, ISoftDeletable
    {
        public string? Title { get; set; }
        public string? Body { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        public DateTime Now => UtcNow;
        public DateTimeOffset OffsetUtcNow => new(UtcNow);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private static readonly DateTime At = new Clock().UtcNow;

    private static void ShouldBeStampedAndWhole(Note? row)
    {
        row.Should().NotBeNull();
        row!.DeletedAt.Should().Be(At);
        row.Title.Should().Be("t", "a column the stub did not carry survives");
        row.Body.Should().Be("kept", "a column the stub did not carry survives");
    }

    [Fact]
    public void A_soft_delete_with_a_partial_instance_keeps_every_stored_column()
    {
        var inner = new InMemoryStore<Note>();
        var guid = inner.Create(new Note { Title = "t", Body = "kept" });
        var stub = new Note { Guid = guid };

        new SoftDeleteStoreWrapper<InMemoryStore<Note>, Note>(inner, new Clock()).Delete(stub);

        ShouldBeStampedAndWhole(inner.Read(guid));
        stub.DeletedAt.Should().Be(At, "the caller's instance is stamped too, as it always was");
    }

    [Fact]
    public async Task An_async_soft_delete_with_a_partial_instance_keeps_every_stored_column()
    {
        var inner = new AsyncInMemoryStore<Note>();
        var guid = await inner.CreateAsync(new Note { Title = "t", Body = "kept" });

        await new AsyncSoftDeleteStoreWrapper<AsyncInMemoryStore<Note>, Note>(inner, new Clock()).DeleteAsync(new Note { Guid = guid });

        ShouldBeStampedAndWhole(await inner.ReadAsync(guid));
    }

    [Fact]
    public void A_bulk_soft_delete_with_partial_instances_keeps_every_stored_column()
    {
        var inner = new InMemoryStore<Note>();
        var a = inner.Create(new Note { Title = "t", Body = "kept" });
        var b = inner.Create(new Note { Title = "t", Body = "kept" });

        new SoftDeleteBulkStoreWrapper<InMemoryStore<Note>, Note>(inner, new Clock()).Delete(new[] { new Note { Guid = a }, new Note { Guid = b } });

        ShouldBeStampedAndWhole(inner.Read(a));
        ShouldBeStampedAndWhole(inner.Read(b));
    }

    [Fact]
    public async Task An_async_bulk_soft_delete_with_partial_instances_keeps_every_stored_column()
    {
        var inner = new AsyncInMemoryStore<Note>();
        var a = await inner.CreateAsync(new Note { Title = "t", Body = "kept" });
        var b = await inner.CreateAsync(new Note { Title = "t", Body = "kept" });

        await new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Note>, Note>(inner, new Clock())
            .DeleteAsync(new[] { new Note { Guid = a }, new Note { Guid = b } });

        ShouldBeStampedAndWhole(await inner.ReadAsync(a));
        ShouldBeStampedAndWhole(await inner.ReadAsync(b));
    }

    [Fact]
    public void A_soft_delete_of_a_key_that_is_not_stored_is_still_reported()
    {
        var store = new SoftDeleteStoreWrapper<InMemoryStore<Note>, Note>(new InMemoryStore<Note>(), new Clock());

        var act = () => store.Delete(new Note { Guid = Guid.NewGuid() });

        act.Should().Throw<EntityNotFoundException>("with no stored row the caller's instance is written, so the store reports the missing key as before (TASK-555)");
    }
}
