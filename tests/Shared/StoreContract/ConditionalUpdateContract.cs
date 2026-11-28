using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// TASK-1013 — the cross-backend contract of <see cref="IAsyncConditionalUpdateStore{T}"/>: a compare-and-set on one
/// entity. A missing id or a condition that does not hold answers <c>null</c> and writes nothing; a condition that holds
/// writes and returns the entity; and of concurrent claims (an update that makes the condition false) exactly one wins.
/// <para>Shared source: linked into each implementing backend's test project, which supplies a fresh store and a model
/// with a name and a claimed flag. Observed state is what the store returns afterwards.</para>
/// </summary>
public abstract class ConditionalUpdateContract<T> where T : AbstractModel
{
    /// <summary>False when the backend is not reachable (a live provider reports its own skip, or fails under
    /// <c>BIRKO_REQUIRE_LIVE</c>).</summary>
    protected virtual bool Ready() => true;

    /// <summary>Makes the store's writes visible to its reads (Elasticsearch refreshes); a no-op elsewhere.</summary>
    protected virtual void Settle() { }

    /// <summary>A fresh, empty store that implements <see cref="IAsyncConditionalUpdateStore{T}"/>.</summary>
    protected abstract IAsyncBulkStore<T> NewStore();

    protected abstract T NewEntity(string name);
    protected abstract string NameOf(T entity);
    protected abstract void Rename(T entity, string name);

    /// <summary>The claim's condition, on the model's flag: <c>x =&gt; !x.Claimed</c>.</summary>
    protected abstract Expression<Func<T, bool>> Unclaimed { get; }

    protected abstract void Claim(T entity);
    protected abstract bool IsClaimed(T entity);

    private static IAsyncConditionalUpdateStore<T> Conditional(IAsyncBulkStore<T> store)
    {
        store.Should().BeAssignableTo<IAsyncConditionalUpdateStore<T>>();
        return (IAsyncConditionalUpdateStore<T>)store;
    }

    private async Task<T?> StoredAsync(IAsyncBulkStore<T> store, Guid guid)
    {
        Settle();
        return await store.ReadAsync(guid);
    }

    [Fact]
    public async Task ConditionalUpdate_of_a_missing_id_answers_null_and_creates_nothing()
    {
        if (!Ready()) return;
        var store = NewStore();
        await store.CreateAsync(NewEntity("kept"));
        var missing = Guid.NewGuid();

        var result = await Conditional(store).UpdateIfAsync(missing, Unclaimed, e => Rename(e, "x"));

        result.Should().BeNull();
        (await StoredAsync(store, missing)).Should().BeNull("a missing entity is not created");
        (await Conditional(store).UpdateIfAsync(Guid.Empty, Unclaimed, e => Rename(e, "x"))).Should().BeNull();
    }

    [Fact]
    public async Task ConditionalUpdate_whose_condition_does_not_hold_answers_null_and_writes_nothing()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("before");
        Claim(entity);
        var id = await store.CreateAsync(entity);

        var result = await Conditional(store).UpdateIfAsync(id, Unclaimed, e => Rename(e, "after"));

        result.Should().BeNull();
        NameOf((await StoredAsync(store, id))!).Should().Be("before");
    }

    [Fact]
    public async Task ConditionalUpdate_whose_condition_holds_writes_and_returns_the_entity()
    {
        if (!Ready()) return;
        var store = NewStore();
        var id = await store.CreateAsync(NewEntity("before"));

        var result = await Conditional(store).UpdateIfAsync(id, Unclaimed, e => { Claim(e); Rename(e, "after"); });

        result.Should().NotBeNull();
        result!.Guid.Should().Be(id);
        IsClaimed(result).Should().BeTrue();
        NameOf(result).Should().Be("after");
        var stored = await StoredAsync(store, id);
        IsClaimed(stored!).Should().BeTrue();
        NameOf(stored!).Should().Be("after");

        (await Conditional(store).UpdateIfAsync(id, Unclaimed, e => Rename(e, "again"))).Should()
            .BeNull("the write made the condition false");
        NameOf((await StoredAsync(store, id))!).Should().Be("after");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    public async Task ConditionalUpdate_concurrent_claims_on_one_entity_have_exactly_one_winner(int claimants)
    {
        if (!Ready()) return;
        var store = NewStore();
        var id = await store.CreateAsync(NewEntity("unclaimed"));
        var conditional = Conditional(store);
        using var start = new ManualResetEventSlim(false);

        var claims = Enumerable.Range(0, claimants).Select(i => Task.Run(async () =>
        {
            start.Wait();
            return await conditional.UpdateIfAsync(id, Unclaimed, e => { Claim(e); Rename(e, "winner-" + i); });
        })).ToList();
        start.Set();
        var results = await Task.WhenAll(claims);

        var winners = results.Where(r => r != null).ToList();
        winners.Should().HaveCount(1, "exactly one of {0} concurrent claims may win", claimants);
        var stored = await StoredAsync(store, id);
        IsClaimed(stored!).Should().BeTrue();
        NameOf(stored!).Should().Be(NameOf(winners[0]!), "the stored entity is the winner's write");
    }

    [Fact]
    public async Task ConditionalUpdate_refuses_a_null_condition_or_update()
    {
        if (!Ready()) return;
        var store = NewStore();
        var id = await store.CreateAsync(NewEntity("x"));

        var noCondition = () => Conditional(store).UpdateIfAsync(id, null!, e => Rename(e, "y"));
        var noUpdate = () => Conditional(store).UpdateIfAsync(id, Unclaimed, null!);

        await noCondition.Should().ThrowAsync<ArgumentNullException>();
        await noUpdate.Should().ThrowAsync<ArgumentNullException>();
        NameOf((await StoredAsync(store, id))!).Should().Be("x");
    }
}
