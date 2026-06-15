using Birko.Configuration;
using Birko.Data.InMemory.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Birko.Data.InMemory.Tests;

public class InMemoryStoreTests
{
    private static InMemoryStore<TestModel> NewStore() => new();

    private static InMemoryStore<TestModel> SeededStore()
    {
        var store = NewStore();
        store.Create(new TestModel { Name = "alpha", Value = 3 });
        store.Create(new TestModel { Name = "beta", Value = 1 });
        store.Create(new TestModel { Name = "gamma", Value = 2 });
        return store;
    }

    [Fact]
    public void Constructor_Default_ShouldNotThrow()
    {
        NewStore().Should().NotBeNull();
    }

    [Fact]
    public void Read_WithoutInit_ShouldReturnEmpty()
    {
        // Lazy-init: CRUD works without an explicit Init() call.
        NewStore().Read().Should().BeEmpty();
    }

    [Fact]
    public void Count_WithoutInit_ShouldReturnZero()
    {
        NewStore().Count().Should().Be(0);
    }

    [Fact]
    public void Create_ShouldAssignGuid_AndPersist()
    {
        var store = NewStore();
        var model = new TestModel { Name = "x", Value = 5 };

        var id = store.Create(model);

        id.Should().NotBe(Guid.Empty);
        model.Guid.Should().Be(id);
        store.Count().Should().Be(1);
        store.Read(id).Should().BeSameAs(model);
    }

    [Fact]
    public void Create_ShouldInvokeStoreDelegate()
    {
        var store = NewStore();
        var called = false;

        store.Create(new TestModel { Name = "x" }, m => { called = true; return m; });

        called.Should().BeTrue();
    }

    [Fact]
    public void Read_WithEmptyGuid_ShouldReturnNull()
    {
        SeededStore().Read(Guid.Empty).Should().BeNull();
    }

    [Fact]
    public void Read_WithUnknownGuid_ShouldReturnNull()
    {
        SeededStore().Read(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void ReadFirst_ShouldReturnSingleMatch_NotCollection()
    {
        var store = SeededStore();

        var result = store.ReadFirst(m => m.Name == "beta");

        result.Should().NotBeNull();
        result!.Name.Should().Be("beta");
    }

    [Fact]
    public void ReadFirst_NoMatch_ShouldReturnNull()
    {
        SeededStore().ReadFirst(m => m.Name == "nope").Should().BeNull();
    }

    [Fact]
    public void Read_BulkFilter_ShouldReturnAllMatches()
    {
        var store = SeededStore();

        var result = store.Read(m => m.Value >= 2).ToList();

        result.Should().HaveCount(2);
        result.Select(m => m.Name).Should().BeEquivalentTo(new[] { "alpha", "gamma" });
    }

    [Fact]
    public void Read_WithOrderByAndPaging_ShouldApplyBoth()
    {
        var store = SeededStore();

        var result = store.Read(null, OrderBy<TestModel>.By(m => m.Value), limit: 2, offset: 0).ToList();

        result.Select(m => m.Value).Should().ContainInOrder(1, 2);
    }

    [Fact]
    public void Count_WithFilter_ShouldCountMatches()
    {
        SeededStore().Count(m => m.Value >= 2).Should().Be(2);
    }

    [Fact]
    public void Update_ShouldMutateStoredEntity()
    {
        var store = NewStore();
        var model = new TestModel { Name = "x", Value = 1 };
        var id = store.Create(model);

        model.Value = 42;
        store.Update(model);

        store.Read(id)!.Value.Should().Be(42);
    }

    [Fact]
    public void Update_UnknownEntity_ShouldBeNoOp()
    {
        var store = SeededStore();
        var orphan = new TestModel { Guid = Guid.NewGuid(), Name = "orphan" };

        store.Update(orphan);

        store.Count().Should().Be(3);
        store.Read(orphan.Guid!.Value).Should().BeNull();
    }

    [Fact]
    public void UpdateByFilter_PropertyUpdate_ShouldApplyToMatches()
    {
        var store = SeededStore();

        store.Update(m => m.Value >= 2, new PropertyUpdate<TestModel>().Set(m => m.Name, "big"));

        store.Read(m => m.Name == "big").Should().HaveCount(2);
    }

    [Fact]
    public void UpdateByFilter_Action_ShouldApplyToMatches()
    {
        var store = SeededStore();

        store.Update(m => m.Value == 1, m => m.Name = "smallest");

        store.ReadFirst(m => m.Name == "smallest").Should().NotBeNull();
    }

    [Fact]
    public void Delete_Entity_ShouldRemove()
    {
        var store = NewStore();
        var id = store.Create(new TestModel { Name = "x" });

        store.Delete(store.Read(id)!);

        store.Count().Should().Be(0);
    }

    [Fact]
    public void DeleteByFilter_ShouldRemoveMatches()
    {
        var store = SeededStore();

        store.Delete(m => m.Value >= 2);

        store.Count().Should().Be(1);
        store.ReadFirst()!.Name.Should().Be("beta");
    }

    [Fact]
    public void CreateBulk_ShouldPersistAll()
    {
        var store = NewStore();

        store.Create(new[]
        {
            new TestModel { Name = "a" },
            new TestModel { Name = "b" },
        });

        store.Count().Should().Be(2);
    }

    [Fact]
    public void Save_ShouldCreateThenUpdate()
    {
        var store = NewStore();
        var model = new TestModel { Name = "x" };

        var id = store.Save(model);
        store.Count().Should().Be(1);

        model.Name = "y";
        var sameId = store.Save(model);

        sameId.Should().Be(id);
        store.Count().Should().Be(1);
        store.Read(id)!.Name.Should().Be("y");
    }

    [Fact]
    public void Destroy_ShouldClearAllData()
    {
        var store = SeededStore();

        store.Destroy();

        store.Count().Should().Be(0);
    }

    [Fact]
    public void Aggregate_ShouldComputeSum()
    {
        var store = SeededStore();

        var query = new AggregateQuery<TestModel>
        {
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, nameof(TestModel.Value), "total") },
        };

        var results = store.Aggregate(query);

        results.Should().HaveCount(1);
        Convert.ToInt32(results[0].Values["total"]).Should().Be(6);
    }

    [Fact]
    public void SetSettings_ShouldNotAffectStorage()
    {
        var store = NewStore();
        store.Create(new TestModel { Name = "x" });

        store.SetSettings(new Settings { Location = "ignored", Name = "ignored" });

        store.Count().Should().Be(1);
    }

    [Fact]
    public void Items_ShouldExposeStoredEntities()
    {
        var store = SeededStore();

        store.Items.Should().HaveCount(3);
        store.Items.Values.Select(m => m.Name).Should().Contain("alpha");
    }
}
