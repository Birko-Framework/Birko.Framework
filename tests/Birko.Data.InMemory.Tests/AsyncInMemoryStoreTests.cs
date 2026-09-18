using Birko.Configuration;
using Birko.Data.InMemory.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.InMemory.Tests;

public class AsyncInMemoryStoreTests
{
    private static AsyncInMemoryStore<TestModel> NewStore() => new();

    private static async Task<AsyncInMemoryStore<TestModel>> SeededStoreAsync()
    {
        var store = NewStore();
        await store.CreateAsync(new TestModel { Name = "alpha", Value = 3 });
        await store.CreateAsync(new TestModel { Name = "beta", Value = 1 });
        await store.CreateAsync(new TestModel { Name = "gamma", Value = 2 });
        return store;
    }

    [Fact]
    public async Task Read_WithoutInit_ShouldReturnEmpty()
    {
        (await NewStore().ReadAsync(filter: null)).Should().BeEmpty();
    }

    [Fact]
    public async Task Count_WithoutInit_ShouldReturnZero()
    {
        (await NewStore().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_ShouldAssignGuid_AndPersist()
    {
        var store = NewStore();
        var model = new TestModel { Name = "x", Value = 5 };

        var id = await store.CreateAsync(model);

        id.Should().NotBe(Guid.Empty);
        model.Guid.Should().Be(id);
        (await store.CountAsync()).Should().Be(1);
        (await store.ReadAsync(id)).Should().BeSameAs(model);
    }

    [Fact]
    public async Task Read_WithUnknownGuid_ShouldReturnNull()
    {
        var store = await SeededStoreAsync();
        (await store.ReadAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ReadFirst_ShouldReturnSingleMatch_NotCollection()
    {
        var store = await SeededStoreAsync();

        var result = await store.ReadFirstAsync(m => m.Name == "beta");

        result.Should().NotBeNull();
        result!.Name.Should().Be("beta");
    }

    [Fact]
    public async Task Read_BulkFilter_ShouldReturnAllMatches()
    {
        var store = await SeededStoreAsync();

        var result = (await store.ReadAsync(m => m.Value >= 2)).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Read_WithOrderByAndPaging_ShouldApplyBoth()
    {
        var store = await SeededStoreAsync();

        var result = (await store.ReadAsync(null, OrderBy<TestModel>.ByDescending(m => m.Value), limit: 2, offset: 0)).ToList();

        result.Select(m => m.Value).Should().ContainInOrder(3, 2);
    }

    [Fact]
    public async Task Update_ShouldMutateStoredEntity()
    {
        var store = NewStore();
        var model = new TestModel { Name = "x", Value = 1 };
        var id = await store.CreateAsync(model);

        model.Value = 42;
        await store.UpdateAsync(model);

        (await store.ReadAsync(id))!.Value.Should().Be(42);
    }

    [Fact]
    public async Task UpdateByFilter_PropertyUpdate_ShouldApplyToMatches()
    {
        var store = await SeededStoreAsync();

        await store.UpdateAsync(m => m.Value >= 2, new PropertyUpdate<TestModel>().Set(m => m.Name, "big"));

        (await store.ReadAsync(m => m.Name == "big")).Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteByFilter_ShouldRemoveMatches()
    {
        var store = await SeededStoreAsync();

        await store.DeleteAsync(m => m.Value >= 2);

        (await store.CountAsync()).Should().Be(1);
        (await store.ReadFirstAsync())!.Name.Should().Be("beta");
    }

    [Fact]
    public async Task CreateBulk_ShouldPersistAll()
    {
        var store = NewStore();

        await store.CreateAsync(new[]
        {
            new TestModel { Name = "a" },
            new TestModel { Name = "b" },
        });

        (await store.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Save_ShouldCreateThenUpdate()
    {
        var store = NewStore();
        var model = new TestModel { Name = "x" };

        var id = await store.SaveAsync(model);
        model.Name = "y";
        var sameId = await store.SaveAsync(model);

        sameId.Should().Be(id);
        (await store.CountAsync()).Should().Be(1);
        (await store.ReadAsync(id))!.Name.Should().Be("y");
    }

    [Fact]
    public async Task Destroy_ShouldClearAllData()
    {
        var store = await SeededStoreAsync();

        await store.DestroyAsync();

        (await store.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Aggregate_ShouldComputeSum()
    {
        var store = await SeededStoreAsync();

        var query = new AggregateQuery<TestModel>
        {
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, nameof(TestModel.Value), "total") },
        };

        var results = await store.AggregateAsync(query);

        results.Should().HaveCount(1);
        results[0].GetValue<int>("total").Should().Be(6);
    }

    [Fact]
    public async Task Operations_ShouldRespectCancellationToken()
    {
        var store = await SeededStoreAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Already-initialized store still observes the cancelled token via the init gate.
        var act = async () => await store.CountAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SetSettings_ShouldNotAffectStorage()
    {
        var store = NewStore();
        await store.CreateAsync(new TestModel { Name = "x" });

        store.SetSettings(new Settings { Location = "ignored", Name = "ignored" });

        (await store.CountAsync()).Should().Be(1);
    }
}
