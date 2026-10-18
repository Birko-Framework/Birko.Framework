using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Caching.Memory;
using FluentAssertions;
using Xunit;

namespace Birko.Caching.Tests.Memory;

/// <summary>
/// TASK-169 on MemoryCache: SH-M038 (a cancelled lock wait leaked its KeyLock reference, so the lock was never
/// retired) and SH-M039 (a stored null answered a read as ANY T, so a null cached by a single-entity read made a
/// collection read under the same key report no rows).
/// </summary>
public class Task169MemoryCacheTests
{
    private static int LockCount(MemoryCache cache) =>
        ((ICollection)typeof(MemoryCache).GetField("_locks", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache)!).Count;

    [Fact]
    public async Task SH_M038_a_cancelled_wait_for_the_key_lock_gives_its_reference_back()
    {
        using var cache = new MemoryCache();
        var factoryEntered = new TaskCompletionSource();
        var releaseFactory = new TaskCompletionSource();
        var holder = cache.GetOrSetAsync("k", async _ =>
        {
            factoryEntered.SetResult();
            await releaseFactory.Task;
            return "v";
        });
        await factoryEntered.Task;

        using var cts = new CancellationTokenSource();
        var waiter = cache.GetOrSetAsync("k", _ => Task.FromResult("other"), ct: cts.Token);
        cts.Cancel();
        await FluentActions.Awaiting(() => waiter).Should().ThrowAsync<OperationCanceledException>();

        releaseFactory.SetResult();
        (await holder).Should().Be("v");

        LockCount(cache).Should().Be(0, "the last releaser retires the lock; a leaked reference pinned it forever");
    }

    [Fact]
    public async Task SH_M039_a_stored_null_does_not_answer_a_read_as_an_unrelated_type()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync<string?>("k", null);

        (await cache.GetAsync<List<int>>("k")).HasValue.Should().BeFalse("a null stored as string is not an empty List<int>");
        (await cache.GetAsync<int>("k")).HasValue.Should().BeFalse("nor a zero");
    }

    [Fact]
    public async Task SH_M039_a_stored_null_is_still_a_hit_for_its_own_type_and_its_base_types()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync<string?>("k", null);

        var asString = await cache.GetAsync<string?>("k");
        asString.HasValue.Should().BeTrue("CR-L036: a stored null is a legitimate hit");
        asString.Value.Should().BeNull();
        (await cache.GetAsync<object?>("k")).HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task SH_M039_GetOrSet_runs_the_factory_for_a_type_the_null_was_not_stored_as()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync<string?>("k", null);

        var list = await cache.GetOrSetAsync("k", _ => Task.FromResult(new List<int> { 1, 2 }));
        list.Should().Equal(1, 2);
    }
}
