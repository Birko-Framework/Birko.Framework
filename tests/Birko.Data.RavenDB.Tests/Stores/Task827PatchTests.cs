using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.RavenDB.UnitOfWork;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// TASK-827 (the user's decision): a <c>PropertyUpdate</c> on RavenDB runs as a server-side patch when every assignment
/// is exact in patch JavaScript (where every number is a double, measured), and stays on the exact read-modify-save path
/// otherwise: a decimal or floating-point value, a computed set, or inside a unit of work.
/// </summary>
public sealed class Task827PatchTests : IDisposable
{
    public enum Level { Low, High }

    public class Counter : AbstractModel
    {
        public string? Name { get; set; }
        public int Hits { get; set; }
        public long Total { get; set; }
        public decimal Amount { get; set; }
        public Guid Ref { get; set; }
        public DateTime When { get; set; }
        public Level Level { get; set; }
    }

    private readonly RavenLive _live;

    public Task827PatchTests(ITestOutputHelper output) => _live = new RavenLive(output);

    public void Dispose() => _live.Dispose();

    [Fact]
    public async Task Parallel_increments_on_one_document_are_not_lost()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Counter>(url, db));
        var guid = await store.CreateAsync(new Counter { Name = "c" });

        // Read-modify-save loses some of these (two writers read the same Hits); a server-side increment cannot.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 10; i++)
                await store.UpdateAsync(x => x.Name == "c", new PropertyUpdate<Counter>().Increment(x => x.Hits, 1));
        })));

        (await store.ReadAsync(guid))!.Hits.Should().Be(80);
    }

    [Fact]
    public void Sync_parallel_increments_are_not_lost()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new RavenDBStore<Counter>(url, db));
        var guid = store.Create(new Counter { Name = "c" });

        Parallel.For(0, 8, _ =>
        {
            for (var i = 0; i < 10; i++)
                store.Update(x => x.Name == "c", new PropertyUpdate<Counter>().Increment(x => x.Hits, 1));
        });

        store.Read(guid)!.Hits.Should().Be(80);
    }

    [Fact]
    public async Task Patched_sets_read_back_exactly_as_CSharp_wrote_them()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Counter>(url, db));
        var guid = await store.CreateAsync(new Counter { Name = "s", Total = 5 });
        var reference = Guid.NewGuid();
        var when = new DateTime(2026, 11, 7, 13, 45, 12, 345, DateTimeKind.Utc);

        await store.UpdateAsync(x => x.Name == "s", new PropertyUpdate<Counter>()
            .Set(x => x.Ref, reference).Set(x => x.When, when).Set(x => x.Level, Level.High)
            .Increment(x => x.Total, 9007199254740000L));

        var after = (await store.ReadAsync(guid))!;
        after.Ref.Should().Be(reference);
        after.When.Should().Be(when);
        after.Level.Should().Be(Level.High);
        after.Total.Should().Be(9007199254740005L);
    }

    [Fact]
    public async Task A_decimal_increment_stays_exact_on_the_CSharp_path()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Counter>(url, db));
        var guid = await store.CreateAsync(new Counter { Name = "d", Amount = 0.1m });

        await store.UpdateAsync(x => x.Name == "d", new PropertyUpdate<Counter>().Increment(x => x.Amount, 0.2m));
        await store.UpdateAsync(x => x.Name == "d", new PropertyUpdate<Counter>().Set(x => x.Amount, (await store.ReadAsync(guid))!.Amount + 98765432109876543.21m));

        (await store.ReadAsync(guid))!.Amount.Should().Be(98765432109876543.51m, "a patch would have computed 0.1 + 0.2 and the wide value in doubles");
    }

    [Fact]
    public async Task An_overflowing_increment_writes_nothing_to_any_document()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Counter>(url, db));
        var low = await store.CreateAsync(new Counter { Name = "o", Hits = 1 });
        var high = await store.CreateAsync(new Counter { Name = "o", Hits = int.MaxValue });

        var act = () => store.UpdateAsync(x => x.Name == "o", new PropertyUpdate<Counter>().Increment(x => x.Hits, 1));

        await act.Should().ThrowAsync<OverflowException>();
        (await store.ReadAsync(low))!.Hits.Should().Be(1, "the patches are one transaction");
        (await store.ReadAsync(high))!.Hits.Should().Be(int.MaxValue);
    }

    [Fact]
    public async Task A_long_past_2_to_the_53_is_refused_rather_than_rounded()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Counter>(url, db));
        var guid = await store.CreateAsync(new Counter { Name = "l", Total = 9007199254740993L }); // 2^53 + 1

        var act = () => store.UpdateAsync(x => x.Name == "l", new PropertyUpdate<Counter>().Increment(x => x.Total, 1L));

        await act.Should().ThrowAsync<OverflowException>();
        (await store.ReadAsync(guid))!.Total.Should().Be(9007199254740993L, "nothing was written");
    }

    [Fact]
    public async Task Inside_a_unit_of_work_the_update_joins_it_and_rolls_back_with_it()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Counter>(url, db));
        var guid = await store.CreateAsync(new Counter { Name = "u", Hits = 3 });

        await using (var uow = RavenDbUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            store.SetTransactionContext(uow.Context);
            await store.UpdateAsync(x => x.Name == "u", new PropertyUpdate<Counter>().Increment(x => x.Hits, 1));
            await uow.RollbackAsync();
            store.SetTransactionContext(null);
        }

        (await store.ReadAsync(guid))!.Hits.Should().Be(3, "a server-side patch would have bypassed the unit of work");
    }

    [Fact]
    public async Task UpdateAll_increments_every_document_on_the_server()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Counter>(url, db));
        var a = await store.CreateAsync(new Counter { Name = "a", Hits = 1 });
        var b = await store.CreateAsync(new Counter { Name = "b", Hits = 2 });

        await store.UpdateAllAsync(new PropertyUpdate<Counter>().Increment(x => x.Hits, 10));

        (await store.ReadAsync(a))!.Hits.Should().Be(11);
        (await store.ReadAsync(b))!.Hits.Should().Be(12);
    }
}
