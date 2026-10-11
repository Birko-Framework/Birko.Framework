using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// CR-H075: AsyncSluggableBulkStoreWrapper.UpdateAsync resolved slugs with Task.WhenAll, firing
/// overlapping ReadAsync calls at the single shared inner store (unsafe for connection-stateful
/// backends) and skipping per-batch uniqueness tracking. It now resolves sequentially with a shared
/// batch set. These tests pin both properties.
/// </summary>
public class AsyncSluggableBulkStoreWrapperTests
{
    private class Article : AbstractModel, ISluggable
    {
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? GetSlugSource() => Title;
    }

    /// <summary>
    /// In-memory store that fails if two reads are ever in flight at once, proving the wrapper
    /// serializes access to the shared inner store.
    /// </summary>
    private class ConcurrencyProbingStore : AsyncInMemoryStore<Article>
    {
        private int _active;
        public bool ConcurrencyDetected { get; private set; }

        protected override async Task<Article?> ReadCoreAsync(
            Expression<Func<Article, bool>>? filter = null, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _active) > 1)
            {
                ConcurrencyDetected = true;
            }
            try
            {
                await Task.Delay(25, ct);
                return await base.ReadCoreAsync(filter, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    [Fact]
    public async Task UpdateAsync_ResolvesSlugsSequentially_NoConcurrentReads()
    {
        var probe = new ConcurrencyProbingStore();
        var wrapper = new AsyncSluggableBulkStoreWrapper<ConcurrencyProbingStore, Article>(probe);

        var items = Enumerable.Range(0, 5)
            .Select(i => new Article { Guid = Guid.NewGuid(), Title = $"Title {i}" })
            .ToList();
        // TASK-555: an update now refuses an entity that is not stored, so the fixture stores them first.
        await probe.CreateAsync(items);

        await wrapper.UpdateAsync(items);

        probe.ConcurrencyDetected.Should().BeFalse();
        items.Should().OnlyContain(a => !string.IsNullOrEmpty(a.Slug));
    }

    [Fact]
    public async Task UpdateAsync_TracksBatchSlugs_ForDuplicateSources()
    {
        var probe = new ConcurrencyProbingStore();
        var wrapper = new AsyncSluggableBulkStoreWrapper<ConcurrencyProbingStore, Article>(probe);

        var a = new Article { Guid = Guid.NewGuid(), Title = "Hello World" };
        var b = new Article { Guid = Guid.NewGuid(), Title = "Hello World" };
        // TASK-555: an update now refuses an entity that is not stored, so the fixture stores them first.
        await probe.CreateAsync(new[] { a, b });

        await wrapper.UpdateAsync(new[] { a, b });

        a.Slug.Should().Be("hello-world");
        b.Slug.Should().NotBe(a.Slug);      // batch tracking gives the second a distinct slug
        b.Slug.Should().StartWith("hello-world");
    }

    [Fact]
    public async Task UpdateAsync_EnumeratesSourceOnce()
    {
        var probe = new ConcurrencyProbingStore();
        var wrapper = new AsyncSluggableBulkStoreWrapper<ConcurrencyProbingStore, Article>(probe);

        var one = new Article { Guid = Guid.NewGuid(), Title = "One" };
        var two = new Article { Guid = Guid.NewGuid(), Title = "Two" };
        // TASK-555: an update now refuses an entity that is not stored, so the fixture stores them first.
        await probe.CreateAsync(new[] { one, two });
        int enumerations = 0;
        IEnumerable<Article> LazySource()
        {
            enumerations++;
            yield return one;
            yield return two;
        }

        await wrapper.UpdateAsync(LazySource());

        enumerations.Should().Be(1);
    }
}
