using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// TASK-632, the sync and collection paths the live race tests do not reach: a store that enforces one unique key (as a
/// unique index does) and lets a concurrent winner in just before the caller's first write. The wrappers retry a refusal
/// they can repair, surface one they cannot, keep the default marker true-or-null, and write a demotion before the
/// promotion it makes room for.
/// </summary>
public class Task632RetryTests
{
    public class Article : AbstractModel, ISluggable
    {
        public string? Name { get; set; }
        public string? Slug { get; set; }
        public string? GetSlugSource() => Name;
    }

    public class Option : AbstractModel, IDefaultMarker
    {
        public string? Name { get; set; }
        public bool IsDefault { get; set; }
        public bool? DefaultMarker { get; set; }
    }

    /// <summary>An in-memory store with one unique key, checked per item in write order, and a one-shot interloper.</summary>
    public sealed class UniqueStore<T> : InMemoryStore<T> where T : AbstractModel
    {
        private readonly Func<T, object?> _key;
        public Action<UniqueStore<T>>? Interloper { get; set; }
        public int Refusals { get; private set; }

        /// <summary>TASK-685: once a write was refused, every read throws this, as PostgreSQL's aborted transaction does.</summary>
        public Exception? ReadFailureAfterRefusal { get; set; }

        public UniqueStore(Func<T, object?> key) => _key = key;

        private void FailIfAborted()
        {
            if (Refusals > 0 && ReadFailureAfterRefusal != null) throw ReadFailureAfterRefusal;
        }

        public override T? Read(Expression<Func<T, bool>>? filter = null) { FailIfAborted(); return base.Read(filter, null, 1, null).FirstOrDefault(); }
        public override IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null) { FailIfAborted(); return base.Read(filter, orderBy, limit, offset); }
        public override long Count(Expression<Func<T, bool>>? filter = null) { FailIfAborted(); return base.Count(filter); }

        public Guid Raw(T item) => base.Create(item);

        private void Before(IEnumerable<T> items, bool create)
        {
            var interloper = Interloper;
            Interloper = null;
            interloper?.Invoke(this);

            var stored = base.Read(null, null, null, null).ToList();
            foreach (var item in items)
            {
                var key = _key(item);
                if (key != null && stored.Any(s => s.Guid != item.Guid && Equals(_key(s), key)))
                {
                    Refusals++;
                    throw create ? StoreException.DuplicateCreate(typeof(T)) : StoreException.UniqueViolation(typeof(T));
                }
                // later items of one batch see this one as stored, demotions included: the check is per statement
                stored.RemoveAll(s => s.Guid == item.Guid);
                stored.Add(item);
            }
        }

        public override Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null) { Before(new[] { data }, true); return base.Create(data, storeDelegate); }
        public override void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null) { var l = data.ToList(); Before(l, true); base.Create(l, storeDelegate); }
        public override void Update(T data, StoreDataDelegate<T>? storeDelegate = null) { Before(new[] { data }, false); base.Update(data, storeDelegate); }
        public override void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null) { var l = data.ToList(); Before(l, false); base.Update(l, storeDelegate); }
    }

    /// <summary>TASK-685: the async twin of <see cref="UniqueStore{T}"/>, refusing a duplicate key, and failing every read
    /// after a refusal with <see cref="ReadFailureAfterRefusal"/>.</summary>
    public sealed class AsyncUniqueStore<T> : AsyncInMemoryStore<T> where T : AbstractModel
    {
        private readonly Func<T, object?> _key;
        public Func<AsyncUniqueStore<T>, Task>? Interloper { get; set; }
        public Exception? ReadFailureAfterRefusal { get; set; }
        public int Refusals { get; private set; }

        public AsyncUniqueStore(Func<T, object?> key) => _key = key;

        public Task<Guid> RawAsync(T item) => base.CreateAsync(item);

        private async Task Before(IEnumerable<T> items, bool create)
        {
            var interloper = Interloper;
            Interloper = null;
            if (interloper != null) await interloper(this);

            var stored = (await base.ReadAsync(null, null, null, null)).ToList();
            foreach (var item in items)
            {
                var key = _key(item);
                if (key != null && stored.Any(s => s.Guid != item.Guid && Equals(_key(s), key)))
                {
                    Refusals++;
                    throw create ? StoreException.DuplicateCreate(typeof(T)) : StoreException.UniqueViolation(typeof(T));
                }
                stored.RemoveAll(s => s.Guid == item.Guid);
                stored.Add(item);
            }
        }

        private void FailIfAborted()
        {
            if (Refusals > 0 && ReadFailureAfterRefusal != null) throw ReadFailureAfterRefusal;
        }

        public override async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) { await Before(new[] { data }, true); return await base.CreateAsync(data, storeDelegate, ct); }
        public override async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) { var l = data.ToList(); await Before(l, true); await base.CreateAsync(l, storeDelegate, ct); }
        public override async Task UpdateAsync(T data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) { await Before(new[] { data }, false); await base.UpdateAsync(data, storeDelegate, ct); }
        public override async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) { var l = data.ToList(); await Before(l, false); await base.UpdateAsync(l, storeDelegate, ct); }
        public override Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) { FailIfAborted(); return base.ReadAsync(filter, ct); }
        public override Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default) { FailIfAborted(); return base.ReadAsync(filter, orderBy, limit, offset, ct); }
        public override Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) { FailIfAborted(); return base.CountAsync(filter, ct); }
    }

    private static UniqueStore<Article> SlugStore() => new(a => a.Slug);
    private static UniqueStore<Option> MarkerStore() => new(o => o.DefaultMarker);

    [Fact]
    public void A_slug_taken_between_probe_and_write_is_retried_with_the_next_suffix()
    {
        var store = SlugStore();
        store.Interloper = s => s.Raw(new Article { Name = "Widget", Slug = "widget" });
        var wrapper = new SluggableStoreWrapper<UniqueStore<Article>, Article>(store);

        var mine = new Article { Name = "Widget" };
        wrapper.Create(mine);

        store.Refusals.Should().Be(1);
        mine.Slug.Should().Be("widget-2");
        store.Read(null, null, null, null).Select(a => a.Slug).Should().BeEquivalentTo(new[] { "widget", "widget-2" });
    }

    [Fact]
    public void A_batch_create_is_retried_while_none_of_it_is_stored()
    {
        var store = SlugStore();
        store.Interloper = s => s.Raw(new Article { Name = "Widget", Slug = "widget" });
        var wrapper = new SluggableBulkStoreWrapper<UniqueStore<Article>, Article>(store);

        wrapper.Create(new[] { new Article { Name = "Widget" }, new Article { Name = "Gadget" } });

        store.Read(null, null, null, null).Select(a => a.Slug).Should().BeEquivalentTo(new[] { "widget", "widget-2", "gadget" });
    }

    [Fact]
    public void A_refusal_for_another_key_surfaces_at_once()
    {
        var store = new UniqueStore<Article>(a => a.Name);
        store.Raw(new Article { Name = "Widget", Slug = "elsewhere" });
        var wrapper = new SluggableStoreWrapper<UniqueStore<Article>, Article>(store);

        wrapper.Invoking(w => w.Create(new Article { Name = "Widget" })).Should().Throw<StoreUniqueViolationException>();
        store.Refusals.Should().Be(1, "the slug was free, so a retry could not repair it");
    }

    [Fact]
    public void A_promotion_beaten_by_a_concurrent_default_unsets_it_and_wins()
    {
        var store = MarkerStore();
        store.Interloper = s => s.Raw(new Option { Name = "theirs", IsDefault = true, DefaultMarker = true });
        var wrapper = new DefaultStoreWrapper<UniqueStore<Option>, Option>(store);

        wrapper.Create(new Option { Name = "mine", IsDefault = true });

        store.Refusals.Should().Be(1);
        var rows = store.Read(null, null, null, null).ToList();
        rows.Single(o => o.IsDefault).Name.Should().Be("mine");
        rows.Single(o => o.Name == "theirs").DefaultMarker.Should().BeNull("a demoted row's marker is null, never false");
        rows.Single(o => o.Name == "mine").DefaultMarker.Should().BeTrue();
    }

    [Fact]
    public void A_batch_that_moves_the_default_writes_the_demotion_first()
    {
        var store = MarkerStore();
        var wrapper = new DefaultStoreWrapper<UniqueStore<Option>, Option>(store);
        var x = new Option { Name = "x", IsDefault = true };
        var y = new Option { Name = "y" };
        wrapper.Create(x);
        wrapper.Create(y);

        y.IsDefault = true;
        x.IsDefault = false;
        wrapper.Update(new[] { y, x });

        store.Refusals.Should().Be(0, "x's demotion lands before y's promotion, so the index never sees two");
        store.Read(null, null, null, null).Single(o => o.IsDefault).Name.Should().Be("y");
    }

    [Fact]
    public void A_filtered_update_that_makes_rows_default_keeps_one()
    {
        var store = MarkerStore();
        var wrapper = new DefaultStoreWrapper<UniqueStore<Option>, Option>(store);
        foreach (var n in new[] { "a", "b", "c" }) wrapper.Create(new Option { Name = n });

        wrapper.Update(o => o.Name != "c", o => o.IsDefault = true);

        var rows = store.Read(null, null, null, null).ToList();
        rows.Count(o => o.IsDefault).Should().Be(1);
        rows.Count(o => o.DefaultMarker == true).Should().Be(1);
        rows.Should().NotContain(o => o.DefaultMarker == false);
    }

    private static readonly InvalidOperationException Aborted = new("25P02: current transaction is aborted");

    private static void ShouldBeTheRefusalNamingTheProbe(Action write)
    {
        var thrown = write.Should().Throw<StoreUniqueViolationException>().Which;
        thrown.Message.Should().Contain("could not query the store after the refusal").And.Contain(Aborted.Message);
        thrown.InnerException.Should().BeAssignableTo<StoreUniqueViolationException>("the original refusal is kept");
    }

    [Fact]
    public void TASK685_a_slug_probe_that_fails_after_the_refusal_surfaces_the_typed_refusal()
    {
        var store = SlugStore();
        store.Interloper = s => s.Raw(new Article { Name = "Widget", Slug = "widget" });
        store.ReadFailureAfterRefusal = Aborted;
        var wrapper = new SluggableStoreWrapper<UniqueStore<Article>, Article>(store);

        ShouldBeTheRefusalNamingTheProbe(() => wrapper.Create(new Article { Name = "Widget" }));
        store.Refusals.Should().Be(1, "nothing is retried once the probe failed");
    }

    [Fact]
    public void TASK685_a_batch_probe_that_fails_after_the_refusal_surfaces_the_typed_refusal()
    {
        var store = SlugStore();
        store.Interloper = s => s.Raw(new Article { Name = "Widget", Slug = "widget" });
        store.ReadFailureAfterRefusal = Aborted;
        var wrapper = new SluggableBulkStoreWrapper<UniqueStore<Article>, Article>(store);

        ShouldBeTheRefusalNamingTheProbe(() => wrapper.Create(new[] { new Article { Guid = Guid.NewGuid(), Name = "Widget" }, new Article { Guid = Guid.NewGuid(), Name = "Gadget" } }));
    }

    [Fact]
    public void TASK685_a_default_probe_that_fails_after_the_refusal_surfaces_the_typed_refusal()
    {
        var store = MarkerStore();
        store.Interloper = s => s.Raw(new Option { Name = "theirs", IsDefault = true, DefaultMarker = true });
        store.ReadFailureAfterRefusal = Aborted;
        var wrapper = new DefaultStoreWrapper<UniqueStore<Option>, Option>(store);

        ShouldBeTheRefusalNamingTheProbe(() => wrapper.Create(new Option { Name = "mine", IsDefault = true }));
    }

    [Fact]
    public void TASK685_a_cancelled_probe_is_not_turned_into_a_refusal()
    {
        var store = SlugStore();
        store.Interloper = s => s.Raw(new Article { Name = "Widget", Slug = "widget" });
        store.ReadFailureAfterRefusal = new OperationCanceledException();
        var wrapper = new SluggableStoreWrapper<UniqueStore<Article>, Article>(store);

        wrapper.Invoking(w => w.Create(new Article { Name = "Widget" })).Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public async Task TASK685_the_async_probe_surfaces_the_refusal_and_lets_cancellation_through()
    {
        var violation = (StoreUniqueViolationException)StoreException.DuplicateCreate(typeof(Article));

        (await StoreException.ProbeAfterRefusalAsync(() => Task.FromResult(true), violation)).Should().BeTrue();
        var thrown = await FluentActions.Awaiting(() => StoreException.ProbeAfterRefusalAsync(() => Task.FromException<bool>(Aborted), violation))
            .Should().ThrowAsync<StoreUniqueViolationException>();
        thrown.Which.InnerException.Should().BeSameAs(violation);
        thrown.Which.Message.Should().Contain(Aborted.Message);
        await FluentActions.Awaiting(() => StoreException.ProbeAfterRefusalAsync(() => Task.FromException<bool>(new OperationCanceledException()), violation))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void TASK685_with_no_read_failure_the_refusal_is_rethrown_as_it_was()
    {
        var violation = (StoreUniqueViolationException)StoreException.UniqueViolation(typeof(Article));

        FluentActions.Invoking(() => StoreException.UnretryableUniqueViolation(violation, null))
            .Should().Throw<StoreUniqueViolationException>().Which.Should().BeSameAs(violation);
    }

    private static async Task ShouldBeTheRefusalNamingTheProbeAsync(Func<Task> write)
    {
        var thrown = (await write.Should().ThrowAsync<StoreUniqueViolationException>()).Which;
        thrown.Message.Should().Contain("could not query the store after the refusal").And.Contain(Aborted.Message);
        thrown.InnerException.Should().BeAssignableTo<StoreUniqueViolationException>("the original refusal is kept");
    }

    private static Task<Guid> TheirSlug(AsyncUniqueStore<Article> s) => s.RawAsync(new Article { Name = "Widget", Slug = "widget" });
    private static Task<Guid> TheirDefault(AsyncUniqueStore<Option> s) => s.RawAsync(new Option { Name = "theirs", IsDefault = true, DefaultMarker = true });

    private static AsyncUniqueStore<Article> AbortingSlugStore() => new(a => a.Slug) { Interloper = TheirSlug, ReadFailureAfterRefusal = Aborted };
    private static AsyncUniqueStore<Option> AbortingMarkerStore() => new(o => o.DefaultMarker) { Interloper = TheirDefault, ReadFailureAfterRefusal = Aborted };

    [Fact]
    public async Task TASK685_async_slug_create_and_update_surface_the_typed_refusal()
    {
        var store = AbortingSlugStore();
        await ShouldBeTheRefusalNamingTheProbeAsync(() => new AsyncSluggableStoreWrapper<AsyncUniqueStore<Article>, Article>(store).CreateAsync(new Article { Name = "Widget" }));
        store.Refusals.Should().Be(1, "nothing is retried once the probe failed");

        store = new AsyncUniqueStore<Article>(a => a.Slug) { ReadFailureAfterRefusal = Aborted };
        var mine = new Article { Name = "Gadget", Slug = "gadget" };
        await store.RawAsync(mine);
        store.Interloper = TheirSlug;
        await ShouldBeTheRefusalNamingTheProbeAsync(() => new AsyncSluggableStoreWrapper<AsyncUniqueStore<Article>, Article>(store)
            .UpdateAsync(new Article { Guid = mine.Guid, Name = "Widget", Slug = "widget" }));
    }

    [Fact]
    public async Task TASK685_async_batch_slug_create_and_update_surface_the_typed_refusal()
    {
        await ShouldBeTheRefusalNamingTheProbeAsync(() => new AsyncSluggableBulkStoreWrapper<AsyncUniqueStore<Article>, Article>(AbortingSlugStore())
            .CreateAsync(new[] { new Article { Guid = Guid.NewGuid(), Name = "Widget" }, new Article { Guid = Guid.NewGuid(), Name = "Gadget" } }));

        var store = new AsyncUniqueStore<Article>(a => a.Slug) { ReadFailureAfterRefusal = Aborted };
        var mine = new Article { Name = "Gadget", Slug = "gadget" };
        await store.RawAsync(mine);
        store.Interloper = TheirSlug;
        await ShouldBeTheRefusalNamingTheProbeAsync(() => new AsyncSluggableBulkStoreWrapper<AsyncUniqueStore<Article>, Article>(store)
            .UpdateAsync(new[] { new Article { Guid = mine.Guid, Name = "Widget", Slug = "widget" } }));
    }

    [Fact]
    public async Task TASK685_async_default_promotion_and_batches_surface_the_typed_refusal()
    {
        await ShouldBeTheRefusalNamingTheProbeAsync(() => new AsyncDefaultStoreWrapper<AsyncUniqueStore<Option>, Option>(AbortingMarkerStore())
            .CreateAsync(new Option { Name = "mine", IsDefault = true }));
        await ShouldBeTheRefusalNamingTheProbeAsync(() => new AsyncDefaultStoreWrapper<AsyncUniqueStore<Option>, Option>(AbortingMarkerStore())
            .CreateAsync(new[] { new Option { Guid = Guid.NewGuid(), Name = "mine", IsDefault = true } }));

        var store = new AsyncUniqueStore<Option>(o => o.DefaultMarker) { ReadFailureAfterRefusal = Aborted };
        var mine = new Option { Name = "mine" };
        await store.RawAsync(mine);
        store.Interloper = TheirDefault;
        await ShouldBeTheRefusalNamingTheProbeAsync(() => new AsyncDefaultStoreWrapper<AsyncUniqueStore<Option>, Option>(store)
            .UpdateAsync(new[] { new Option { Guid = mine.Guid, Name = "mine", IsDefault = true } }));
    }

    [Fact]
    public void TASK685_sync_updates_and_default_batches_surface_the_typed_refusal()
    {
        var slugs = SlugStore();
        var mine = new Article { Name = "Gadget", Slug = "gadget" };
        slugs.Raw(mine);
        slugs.Interloper = s => s.Raw(new Article { Name = "Widget", Slug = "widget" });
        slugs.ReadFailureAfterRefusal = Aborted;
        ShouldBeTheRefusalNamingTheProbe(() => new SluggableBulkStoreWrapper<UniqueStore<Article>, Article>(slugs)
            .Update(new[] { new Article { Guid = mine.Guid, Name = "Widget", Slug = "widget" } }));

        var single = SlugStore();
        var gadget = new Article { Name = "Gadget", Slug = "gadget" };
        single.Raw(gadget);
        single.Interloper = s => s.Raw(new Article { Name = "Widget", Slug = "widget" });
        single.ReadFailureAfterRefusal = Aborted;
        ShouldBeTheRefusalNamingTheProbe(() => new SluggableStoreWrapper<UniqueStore<Article>, Article>(single)
            .Update(new Article { Guid = gadget.Guid, Name = "Widget", Slug = "widget" }));

        var markers = MarkerStore();
        markers.Interloper = s => s.Raw(new Option { Name = "theirs", IsDefault = true, DefaultMarker = true });
        markers.ReadFailureAfterRefusal = Aborted;
        ShouldBeTheRefusalNamingTheProbe(() => new DefaultStoreWrapper<UniqueStore<Option>, Option>(markers)
            .Create(new[] { new Option { Guid = Guid.NewGuid(), Name = "mine", IsDefault = true } }));

        markers = MarkerStore();
        var option = new Option { Name = "mine" };
        markers.Raw(option);
        markers.Interloper = s => s.Raw(new Option { Name = "theirs", IsDefault = true, DefaultMarker = true });
        markers.ReadFailureAfterRefusal = Aborted;
        ShouldBeTheRefusalNamingTheProbe(() => new DefaultStoreWrapper<UniqueStore<Option>, Option>(markers)
            .Update(new[] { new Option { Guid = option.Guid, Name = "mine", IsDefault = true } }));
    }
}
