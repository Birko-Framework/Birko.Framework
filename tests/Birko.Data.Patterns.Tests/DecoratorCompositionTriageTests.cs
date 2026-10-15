using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;
using Post = Birko.Data.Patterns.Tests.PropertyUpdateIncrementDecoratorTests.Post;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// TASK-159, the store-decorator-composition medium findings. Each test is measured on the state the inner store holds
/// afterwards, or on the caller's own objects, because that is where each defect showed.
/// </summary>
public class DecoratorCompositionTriageTests
{
    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime Now { get; set; } = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
        public DateTimeOffset OffsetUtcNow => new(Now);
        public DateOnly Today => DateOnly.FromDateTime(Now);
    }

    private sealed class FixedUser : IAuditContext
    {
        public Guid? CurrentUserGuid { get; } = Guid.NewGuid();
    }

    public class Option : AbstractModel, IDefault
    {
        public string? Name { get; set; }
        public bool IsDefault { get; set; }
    }

    // ---------------- SH-M322: a decorator stamps a copy of the caller's PropertyUpdate ----------------

    [Fact]
    public void Audit_filtered_update_leaves_the_callers_PropertyUpdate_as_it_was()
    {
        var inner = new InMemoryStore<Post>();
        inner.Create(new Post { Title = "a" });
        var store = new AuditBulkStoreWrapper<InMemoryStore<Post>, Post>(inner, new FixedUser());
        var updates = new PropertyUpdate<Post>().Set(x => x.Title, "b");

        store.Update(x => x.Title == "a", updates);

        updates.Assignments.Select(a => a.MemberPath).Should().Equal("Title");
    }

    [Fact]
    public async Task Timestamp_filtered_update_leaves_the_callers_PropertyUpdate_as_it_was()
    {
        var inner = new AsyncInMemoryStore<Post>();
        await inner.CreateAsync(new Post { Title = "a" });
        var store = new AsyncTimestampBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, new FixedClock());
        var updates = new PropertyUpdate<Post>().Increment(x => x.Views, 1);

        await store.UpdateAsync(x => x.Title == "a", updates);

        updates.Assignments.Select(a => a.MemberPath).Should().Equal("Views");
    }

    // ---------------- SH-M323: a lazy batch is enumerated once ----------------

    [Fact]
    public void Soft_delete_of_a_lazy_batch_enumerates_it_once()
    {
        var inner = new InMemoryStore<Post>();
        var stored = new[] { new Post { Title = "a" }, new Post { Title = "b" } };
        inner.Create(stored);
        var store = new SoftDeleteBulkStoreWrapper<InMemoryStore<Post>, Post>(inner, new FixedClock());
        var enumerations = 0;
        IEnumerable<Post> Batch() { enumerations++; foreach (var p in stored) yield return p; }

        store.Delete(Batch());

        enumerations.Should().Be(1);
        inner.Read(x => x.DeletedAt != null, null, null, null).Should().HaveCount(2);
    }

    // ---------------- SH-M319: the sync bulk slug update counts the batch's own slugs ----------------

    [Fact]
    public void Sync_bulk_update_gives_two_rows_of_one_batch_distinct_slugs()
    {
        var inner = new InMemoryStore<Post>();
        var a = new Post { Title = "one", Slug = "one" };
        var b = new Post { Title = "two", Slug = "two" };
        inner.Create(new[] { a, b });
        var store = new SluggableBulkStoreWrapper<InMemoryStore<Post>, Post>(inner);
        a.Slug = null; a.Title = "Widget";
        b.Slug = null; b.Title = "Widget";

        store.Update(new[] { a, b });

        inner.Read(x => x.Title == "Widget", null, null, null).Select(p => p.Slug).Should().OnlyHaveUniqueItems();
    }

    // ---------------- SH-M326: the slug probe asks for ANOTHER row, not for one row ----------------

    /// <summary>
    /// Answers a single filtered read with the PREFERRED row when it matches. The defect depended on which matching row
    /// the store happened to return, and a dictionary's order is not one a test can choose, so the test chooses it.
    /// </summary>
    private sealed class SelfFirstStore : InMemoryStore<Post>
    {
        public Guid? Preferred { get; set; }

        protected override Post? ReadCore(System.Linq.Expressions.Expression<Func<Post, bool>>? filter = null) =>
            ReadCore(filter, null, null, null).OrderBy(p => p.Guid == Preferred ? 0 : 1).FirstOrDefault();
    }

    private sealed class AsyncSelfFirstStore : AsyncInMemoryStore<Post>
    {
        public Guid? Preferred { get; set; }

        protected override async Task<Post?> ReadCoreAsync(System.Linq.Expressions.Expression<Func<Post, bool>>? filter = null, System.Threading.CancellationToken ct = default) =>
            (await ReadCoreAsync(filter, null, null, null, ct)).OrderBy(p => p.Guid == Preferred ? 0 : 1).FirstOrDefault();
    }

    [Fact]
    public void An_update_does_not_keep_a_slug_another_row_holds()
    {
        var inner = new SelfFirstStore();
        var updated = new Post { Title = "Widget", Slug = "widget" };
        inner.Create(updated);
        inner.Create(new Post { Title = "Widget", Slug = "widget" });
        inner.Preferred = updated.Guid;
        var store = new SluggableStoreWrapper<InMemoryStore<Post>, Post>(inner);

        store.Update(new Post { Guid = updated.Guid, Title = "Widget", Slug = "widget" });

        inner.Read(updated.Guid!.Value)!.Slug.Should().NotBe("widget", "the other row holds it");
    }

    [Fact]
    public async Task An_async_update_does_not_keep_a_slug_another_row_holds()
    {
        var inner = new AsyncSelfFirstStore();
        var updated = new Post { Title = "Widget", Slug = "widget" };
        await inner.CreateAsync(updated);
        await inner.CreateAsync(new Post { Title = "Widget", Slug = "widget" });
        inner.Preferred = updated.Guid;
        var store = new AsyncSluggableStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner);

        await store.UpdateAsync(new Post { Guid = updated.Guid, Title = "Widget", Slug = "widget" });

        (await inner.ReadAsync(updated.Guid!.Value))!.Slug.Should().NotBe("widget", "the other row holds it");
    }

    [Fact]
    public void An_update_keeps_its_own_slug_when_no_other_row_holds_it()
    {
        var inner = new InMemoryStore<Post>();
        var post = new Post { Title = "Widget", Slug = "widget" };
        inner.Create(post);
        var store = new SluggableStoreWrapper<InMemoryStore<Post>, Post>(inner);

        store.Update(post);

        inner.Read(post.Guid!.Value)!.Slug.Should().Be("widget");
    }

    // ---------------- SH-M320 / M329 / M330: the single-default rule on filtered writes ----------------

    private static InMemoryStore<Option> Options(int count)
    {
        var inner = new InMemoryStore<Option>();
        for (var i = 0; i < count; i++) inner.Create(new Option { Name = "o" + i });
        return inner;
    }

    private static async Task<AsyncInMemoryStore<Option>> OptionsAsync(int count)
    {
        var inner = new AsyncInMemoryStore<Option>();
        for (var i = 0; i < count; i++) await inner.CreateAsync(new Option { Name = "o" + i });
        return inner;
    }

    [Fact]
    public void A_property_update_setting_IsDefault_over_many_rows_leaves_one_default()
    {
        var inner = Options(3);
        var store = new DefaultStoreWrapper<InMemoryStore<Option>, Option>(inner);

        store.Update(x => x.Name != null, new PropertyUpdate<Option>().Set(x => x.IsDefault, true));

        inner.Read(x => x.IsDefault, null, null, null).Should().HaveCount(1);
    }

    [Fact]
    public async Task An_async_property_update_setting_IsDefault_over_many_rows_leaves_one_default()
    {
        var inner = await OptionsAsync(3);
        var store = new AsyncDefaultStoreWrapper<AsyncInMemoryStore<Option>, Option>(inner);

        await store.UpdateAsync(x => x.Name != null, new PropertyUpdate<Option>().Set(x => x.IsDefault, true));

        (await inner.ReadAsync(x => x.IsDefault, null, null, null)).Should().HaveCount(1);
    }

    [Fact]
    public void A_property_update_not_touching_IsDefault_is_applied_as_given()
    {
        var inner = Options(3);
        var store = new DefaultStoreWrapper<InMemoryStore<Option>, Option>(inner);

        store.Update(x => x.Name != null, new PropertyUpdate<Option>().Set(x => x.Name, "renamed"));

        inner.Read(x => x.Name == "renamed", null, null, null).Should().HaveCount(3);
    }

    [Fact]
    public void A_filtered_action_making_every_match_default_leaves_one_default()
    {
        var inner = Options(3);
        var store = new DefaultStoreWrapper<InMemoryStore<Option>, Option>(inner);

        store.Update(x => x.Name != null, o => o.IsDefault = true);

        inner.Read(x => x.IsDefault, null, null, null).Should().HaveCount(1);
    }

    [Fact]
    public async Task An_async_filtered_action_making_every_match_default_leaves_one_default()
    {
        var inner = await OptionsAsync(3);
        var store = new AsyncDefaultStoreWrapper<AsyncInMemoryStore<Option>, Option>(inner);

        await store.UpdateAsync(x => x.Name != null, o => o.IsDefault = true);

        (await inner.ReadAsync(x => x.IsDefault, null, null, null)).Should().HaveCount(1);
    }

    [Fact]
    public void A_filtered_default_update_unsets_a_default_outside_the_filter()
    {
        var inner = Options(2);
        inner.Create(new Option { Name = "old", IsDefault = true });
        var store = new DefaultStoreWrapper<InMemoryStore<Option>, Option>(inner);

        store.Update(x => x.Name == "o0", o => o.IsDefault = true);

        inner.Read(x => x.IsDefault, null, null, null).Select(o => o.Name).Should().Equal("o0");
    }

    [Fact]
    public void A_filtered_default_update_refuses_an_unbounded_filter()
    {
        var store = new DefaultStoreWrapper<InMemoryStore<Option>, Option>(Options(2));
        var none = new List<string>();

        var act = () => store.Update(x => !none.Contains(x.Name!), o => o.IsDefault = true);

        act.Should().Throw<WholeTableWriteException>();
    }

    // ---------------- SH-M325: a deleted row keeps its deletion instant ----------------

    [Fact]
    public void Deleting_an_already_deleted_row_keeps_its_DeletedAt()
    {
        var clock = new FixedClock();
        var inner = new InMemoryStore<Post>();
        var store = new SoftDeleteStoreWrapper<InMemoryStore<Post>, Post>(inner, clock);
        var post = new Post { Title = "a" };
        store.Create(post);
        store.Delete(post);
        var first = inner.Read(post.Guid!.Value)!.DeletedAt;

        clock.Now = clock.Now.AddDays(3);
        store.Delete(inner.Read(post.Guid!.Value)!);

        inner.Read(post.Guid!.Value)!.DeletedAt.Should().Be(first);
    }

    [Fact]
    public async Task Async_deleting_an_already_deleted_row_keeps_its_DeletedAt()
    {
        var clock = new FixedClock();
        var inner = new AsyncInMemoryStore<Post>();
        var store = new AsyncSoftDeleteStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, clock);
        var post = new Post { Title = "a" };
        await store.CreateAsync(post);
        await store.DeleteAsync(post);
        var first = (await inner.ReadAsync(post.Guid!.Value))!.DeletedAt;

        clock.Now = clock.Now.AddDays(3);
        await store.DeleteAsync((await inner.ReadAsync(post.Guid!.Value))!);

        (await inner.ReadAsync(post.Guid!.Value))!.DeletedAt.Should().Be(first);
    }

    [Fact]
    public async Task Async_bulk_delete_stamps_only_the_live_rows()
    {
        var clock = new FixedClock();
        var inner = new AsyncInMemoryStore<Post>();
        var store = new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, clock);
        var deleted = new Post { Title = "gone" };
        var live = new Post { Title = "live" };
        await store.CreateAsync(new[] { deleted, live });
        await store.DeleteAsync(deleted);
        var first = (await inner.ReadAsync(deleted.Guid!.Value))!.DeletedAt;

        clock.Now = clock.Now.AddDays(3);
        await store.DeleteAsync(new[] { (await inner.ReadAsync(deleted.Guid!.Value))!, live });

        (await inner.ReadAsync(deleted.Guid!.Value))!.DeletedAt.Should().Be(first);
        (await inner.ReadAsync(live.Guid!.Value))!.DeletedAt.Should().Be(clock.Now);
    }

    // ---------------- SH-M331: a soft-deleted row is not found by an update ----------------

    [Fact]
    public void Updating_a_soft_deleted_row_is_not_found_and_does_not_resurrect_it()
    {
        var inner = new InMemoryStore<Post>();
        var store = new SoftDeleteStoreWrapper<InMemoryStore<Post>, Post>(inner, new FixedClock());
        var post = new Post { Title = "a" };
        store.Create(post);
        store.Delete(post);

        var act = () => store.Update(new Post { Guid = post.Guid, Title = "back", DeletedAt = null });

        act.Should().Throw<EntityNotFoundException>();
        inner.Read(post.Guid!.Value)!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Async_updating_a_soft_deleted_row_is_not_found_and_does_not_resurrect_it()
    {
        var inner = new AsyncInMemoryStore<Post>();
        var store = new AsyncSoftDeleteStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, new FixedClock());
        var post = new Post { Title = "a" };
        await store.CreateAsync(post);
        await store.DeleteAsync(post);

        var act = () => store.UpdateAsync(new Post { Guid = post.Guid, Title = "back", DeletedAt = null });

        await act.Should().ThrowAsync<EntityNotFoundException>();
        (await inner.ReadAsync(post.Guid!.Value))!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void A_bulk_update_writes_the_live_rows_and_reports_the_deleted_one()
    {
        var inner = new InMemoryStore<Post>();
        var store = new SoftDeleteBulkStoreWrapper<InMemoryStore<Post>, Post>(inner, new FixedClock());
        var gone = new Post { Title = "gone" };
        var live = new Post { Title = "live" };
        store.Create(new[] { gone, live });
        store.Delete(gone);

        var act = () => store.Update(new[]
        {
            new Post { Guid = gone.Guid, Title = "back" },
            new Post { Guid = live.Guid, Title = "live2" },
        });

        act.Should().Throw<EntityNotFoundException>().Which.Guids.Should().Equal(gone.Guid!.Value);
        inner.Read(gone.Guid!.Value)!.DeletedAt.Should().NotBeNull();
        inner.Read(live.Guid!.Value)!.Title.Should().Be("live2");
    }

    // ---------------- SH-M332 / M333: a filtered soft delete checks the caller's filter ----------------

    [Fact]
    public void A_filtered_soft_delete_refuses_an_unbounded_filter()
    {
        var inner = new InMemoryStore<Post>();
        inner.Create(new Post { Title = "a" });
        var store = new SoftDeleteBulkStoreWrapper<InMemoryStore<Post>, Post>(inner, new FixedClock());
        var none = new List<string>();

        var act = () => store.Delete(x => !none.Contains(x.Title!));

        act.Should().Throw<WholeTableWriteException>();
        inner.Read(x => x.DeletedAt != null, null, null, null).Should().BeEmpty();
    }

    [Fact]
    public async Task An_async_filtered_soft_delete_refuses_a_null_filter()
    {
        var inner = new AsyncInMemoryStore<Post>();
        await inner.CreateAsync(new Post { Title = "a" });
        var store = new AsyncSoftDeleteBulkStoreWrapper<AsyncInMemoryStore<Post>, Post>(inner, new FixedClock());

        var act = () => store.DeleteAsync((System.Linq.Expressions.Expression<Func<Post, bool>>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
        (await inner.ReadAsync(x => x.DeletedAt != null, null, null, null)).Should().BeEmpty();
    }

    [Fact]
    public void A_filtered_soft_delete_stamps_the_matching_live_rows_and_keeps_a_deleted_ones_instant()
    {
        var clock = new FixedClock();
        var inner = new InMemoryStore<Post>();
        var store = new SoftDeleteBulkStoreWrapper<InMemoryStore<Post>, Post>(inner, clock);
        var old = new Post { Title = "x" };
        var live = new Post { Title = "x" };
        store.Create(new[] { old, live });
        store.Delete(old);
        var first = inner.Read(old.Guid!.Value)!.DeletedAt;

        clock.Now = clock.Now.AddDays(1);
        store.Delete(x => x.Title == "x");

        inner.Read(old.Guid!.Value)!.DeletedAt.Should().Be(first);
        inner.Read(live.Guid!.Value)!.DeletedAt.Should().Be(clock.Now);
    }
}
