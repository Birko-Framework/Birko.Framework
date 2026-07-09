using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Repositories.Tests;

/// <summary>
/// CR-H081: RepositoryLocator claimed thread-safety but did the final cache read outside the lock
/// and mutated the plain (non-concurrent) Dictionary in Destroy with no lock at all — undefined
/// behavior under contention (torn reads / InvalidOperationException / corruption). This stress test
/// hammers create+read+destroy concurrently and asserts it neither throws nor tears the cache.
/// </summary>
public class RepositoryLocatorConcurrencyTests
{
    public sealed class FakeStore : IBaseStore
    {
        public void Init() { }
        public void Destroy() { }
    }

    public sealed class FakeRepository : IBaseRepository
    {
        public FakeStore Store { get; }
        public FakeRepository(FakeStore store) => Store = store;
        public void Destroy() { }
    }

    [Fact]
    public void ConcurrentGet_ReturnsSameCachedInstance_WithoutThrowing()
    {
        var store = new FakeStore();
        const string key = "concurrency-same-instance";

        var results = new FakeRepository[64];
        Parallel.For(0, results.Length, i =>
        {
            results[i] = RepositoryLocator.GetRepository<FakeStore, FakeRepository>(store, key);
        });

        results.Should().OnlyContain(r => ReferenceEquals(r, results[0]));

        RepositoryLocator.Destroy<FakeRepository>(key);
    }

    [Fact]
    public async Task ConcurrentGetAndDestroy_DoesNotThrow()
    {
        var store = new FakeStore();
        Exception? failure = null;

        var tasks = Enumerable.Range(0, 16).Select(t => Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    var key = "k" + (i % 8);
                    RepositoryLocator.GetRepository<FakeStore, FakeRepository>(store, key);
                    if (i % 3 == 0)
                    {
                        RepositoryLocator.Destroy<FakeRepository>(key);
                    }
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref failure, ex, null);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        failure.Should().BeNull();
    }
}
