using System;
using System.Collections.Generic;
using System.Linq;
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

        public UniqueStore(Func<T, object?> key) => _key = key;

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
}
