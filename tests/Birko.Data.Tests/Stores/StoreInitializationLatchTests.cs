using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Tests.Stores
{
    /// <summary>
    /// TASK-163 — the store bases' initialization latch.
    /// <para><b>SH-M307 (SLI-1):</b> <c>Destroy</c> was abstract and never cleared the private latch, so a store whose
    /// destroy removed its storage (a JSON file, a Mongo collection, a SQL table) skipped <c>InitCore</c> on the next
    /// operation and worked against storage that no longer existed. <c>Destroy</c> is now a non-virtual wrapper around
    /// <c>DestroyCore</c> that resets the latch.</para>
    /// <para><b>SH-M316 (SLI-2/SLI-3):</b> a CRUD call from inside <c>InitCore</c> re-entered the init — the sync base
    /// recursed to a StackOverflowException, the async base deadlocked on its SemaphoreSlim. Now the re-entrant call
    /// passes through on the initializing flow, the same in both bases. The doubles below refuse a second
    /// <c>InitCore</c> and the async tests carry a timeout, so the old code fails these tests instead of killing or
    /// hanging the test host.</para>
    /// </summary>
    public class StoreInitializationLatchTests
    {
        private class Item : AbstractModel
        {
            public string Name { get; set; } = string.Empty;
        }

        private sealed class SyncStore : AbstractStore<Item>
        {
            public readonly ConcurrentDictionary<Guid, Item> Data = new();
            public int Inits;
            public bool SeedInInit;
            public Exception? DestroyThrows;
            public bool DestroyCallsPublicDestroy;
            public ManualResetEventSlim? HoldInit;
            public ManualResetEventSlim InitEntered { get; } = new();

            protected override void InitCore()
            {
                if (Interlocked.Increment(ref Inits) > 1 && SeedInInit)
                    throw new InvalidOperationException("InitCore re-entered");
                InitEntered.Set();
                HoldInit?.Wait(TimeSpan.FromSeconds(10));
                if (SeedInInit && Data.IsEmpty)
                    Create(new Item { Name = "seed" }); // the re-entrant call
            }

            protected override void DestroyCore()
            {
                if (DestroyCallsPublicDestroy) Destroy(); // the pre-TASK-163 base.Destroy() shape
                Data.Clear();
                if (DestroyThrows != null) throw DestroyThrows;
            }

            protected override Guid CreateCore(Item data, StoreDataDelegate<Item>? processDelegate = null)
            {
                data.Guid = Guid.NewGuid();
                Data[data.Guid.Value] = data;
                return data.Guid.Value;
            }

            protected override Item? ReadCore(Expression<Func<Item, bool>>? filter = null)
                => filter == null ? Data.Values.FirstOrDefault() : Data.Values.FirstOrDefault(filter.Compile());

            protected override long CountCore(Expression<Func<Item, bool>>? filter = null) => Data.Count;
            protected override void UpdateCore(Item data, StoreDataDelegate<Item>? processDelegate = null) { }
            protected override void DeleteCore(Item data) { }
            public override Item CreateInstance() => new();
        }

        private sealed class AsyncStore : AbstractAsyncStore<Item>
        {
            public readonly ConcurrentDictionary<Guid, Item> Data = new();
            public int Inits;
            public bool SeedInInit;
            public Exception? DestroyThrows;
            public bool DestroyCallsPublicDestroy;

            protected override async Task InitCoreAsync(CancellationToken ct = default)
            {
                if (Interlocked.Increment(ref Inits) > 1 && SeedInInit)
                    throw new InvalidOperationException("InitCoreAsync re-entered");
                await Task.Yield();
                if (SeedInInit && Data.IsEmpty)
                    await CreateAsync(new Item { Name = "seed" }, ct: ct); // the re-entrant call
            }

            protected override Task DestroyCoreAsync(CancellationToken ct = default)
            {
                if (DestroyCallsPublicDestroy) return DestroyAsync(ct); // the pre-TASK-163 base.DestroyAsync() shape
                Data.Clear();
                return DestroyThrows != null ? Task.FromException(DestroyThrows) : Task.CompletedTask;
            }

            protected override Task<Guid> CreateCoreAsync(Item data, StoreDataDelegate<Item>? processDelegate = null, CancellationToken ct = default)
            {
                data.Guid = Guid.NewGuid();
                Data[data.Guid.Value] = data;
                return Task.FromResult(data.Guid.Value);
            }

            protected override Task<Item?> ReadCoreAsync(Expression<Func<Item, bool>>? filter = null, CancellationToken ct = default)
                => Task.FromResult(Data.Values.FirstOrDefault());

            protected override Task<long> CountCoreAsync(Expression<Func<Item, bool>>? filter = null, CancellationToken ct = default)
                => Task.FromResult((long)Data.Count);

            protected override Task UpdateCoreAsync(Item data, StoreDataDelegate<Item>? processDelegate = null, CancellationToken ct = default) => Task.CompletedTask;
            protected override Task DeleteCoreAsync(Item data, CancellationToken ct = default) => Task.CompletedTask;
            public override Item CreateInstance() => new();
        }

        private static readonly TimeSpan Hang = TimeSpan.FromSeconds(5);

        // ── SH-M307 ──

        [Fact]
        public void Destroy_ResetsTheLatch_SoTheNextOperationInitializesAgain()
        {
            var store = new SyncStore();
            store.Create(new Item());
            store.Inits.Should().Be(1);

            store.Destroy();
            store.Create(new Item());

            store.Inits.Should().Be(2, "storage removed by Destroy must be provisioned again");
        }

        [Fact]
        public void Destroy_ThatThrows_StillResetsTheLatch()
        {
            var store = new SyncStore { DestroyThrows = new InvalidOperationException("half-destroyed") };
            store.Init();

            var act = () => store.Destroy();

            act.Should().Throw<InvalidOperationException>().WithMessage("half-destroyed");
            store.Count();
            store.Inits.Should().Be(2, "storage in an unknown state is re-provisioned, not trusted");
        }

        [Fact]
        public async Task DestroyAsync_ResetsTheLatch_SoTheNextOperationInitializesAgain()
        {
            var store = new AsyncStore();
            await store.CreateAsync(new Item());

            await store.DestroyAsync();
            await store.CreateAsync(new Item());

            store.Inits.Should().Be(2);
        }

        [Fact]
        public async Task DestroyAsync_ThatThrows_StillResetsTheLatch()
        {
            var store = new AsyncStore { DestroyThrows = new InvalidOperationException("half-destroyed") };
            await store.InitAsync();

            var act = async () => await store.DestroyAsync();

            await act.Should().ThrowAsync<InvalidOperationException>();
            await store.CountAsync();
            store.Inits.Should().Be(2);
        }

        [Fact]
        public void DestroyCore_CallingDestroy_IsRefused_NotARecursion()
        {
            var store = new SyncStore { DestroyCallsPublicDestroy = true };

            var act = () => store.Destroy();

            act.Should().Throw<InvalidOperationException>().WithMessage("*base.DestroyCore*");
            store.DestroyCallsPublicDestroy = false;
            store.Invoking(s => s.Destroy()).Should().NotThrow("the guard is released after a refused call");
        }

        [Fact]
        public async Task DestroyCoreAsync_CallingDestroyAsync_IsRefused_NotARecursion()
        {
            var store = new AsyncStore { DestroyCallsPublicDestroy = true };

            var act = async () => await store.DestroyAsync().WaitAsync(Hang);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*base.DestroyCoreAsync*");
            store.DestroyCallsPublicDestroy = false;
            await store.DestroyAsync().WaitAsync(Hang);
        }

        // ── SH-M316 ──

        [Fact]
        public void InitCore_ThatSeedsThroughCreate_RunsOnce_AndTheSeedIsStored()
        {
            var store = new SyncStore { SeedInInit = true };

            store.Init();

            store.Inits.Should().Be(1, "the Create inside InitCore must not start a second init");
            store.Data.Values.Select(i => i.Name).Should().Equal("seed");
            store.Create(new Item { Name = "after" });
            store.Inits.Should().Be(1, "and the latch still holds afterwards");
        }

        [Fact]
        public async Task InitCoreAsync_ThatSeedsThroughCreateAsync_RunsOnce_WithoutDeadlocking()
        {
            var store = new AsyncStore { SeedInInit = true };

            await store.InitAsync().WaitAsync(Hang);

            store.Inits.Should().Be(1);
            store.Data.Values.Select(i => i.Name).Should().Equal("seed");
            await store.CreateAsync(new Item { Name = "after" }).WaitAsync(Hang);
            store.Inits.Should().Be(1);
        }

        [Fact]
        public async Task The_reentrancy_pass_is_per_call_flow_another_thread_still_waits_for_init()
        {
            var store = new SyncStore { HoldInit = new ManualResetEventSlim(false) };
            var initializing = Task.Run(() => store.Init());
            store.InitEntered.Wait(Hang).Should().BeTrue();

            var other = Task.Run(() => store.Count());
            (await Task.WhenAny(other, Task.Delay(150))).Should().NotBeSameAs(other,
                "a different flow must not slip through while InitCore is still running");

            store.HoldInit.Set();
            await Task.WhenAll(initializing, other).WaitAsync(Hang);
            store.Inits.Should().Be(1);
        }
    }
}
