using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Repositories;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.SQL.ViewModel.Tests;

public class TestModel : AbstractModel
{
    public string Name { get; set; } = string.Empty;
}

public class TestViewModel : ILoadable<TestModel>
{
    public Guid? Guid { get; set; }
    public string Name { get; set; } = string.Empty;
    public void LoadFrom(TestModel data)
    {
        Guid = data.Guid;
        Name = data.Name;
    }
}

public class TestRepo : AsyncDataBaseRepository<SqLiteConnector, TestViewModel, TestModel>
{
    public TestRepo(IAsyncBulkStore<TestModel>? store) : base(store) { }
    protected override void MapToModel(TestViewModel source, TestModel target)
    {
        target.Guid = source.Guid;
        target.Name = source.Name;
    }
}

/// <summary>
/// Regression tests for CR-C17: AsyncDataBaseRepository hard-coded AbstractConnector in its generic
/// store type-check. Because C# generics are invariant, a concrete
/// AsyncDataBaseBulkStore&lt;SqLiteConnector, T&gt; is not an AsyncDataBaseBulkStore&lt;AbstractConnector, T&gt;,
/// so the constructor threw for every real store and DataBaseStore always returned null. The repo is
/// now generic over TConnector (mirroring the sync DataBaseRepository).
/// </summary>
public class AsyncDataBaseRepositoryTests
{
    [Fact]
    public void Constructor_AcceptsConcreteConnectorStore_DoesNotThrow()
    {
        var store = new AsyncSQLiteStore<TestModel>(); // constructed, not connected — the ctor only type-checks
        Action act = () => new TestRepo(store);
        act.Should().NotThrow();
    }

    [Fact]
    public void DataBaseStore_ResolvesTheConcreteStore()
    {
        var store = new AsyncSQLiteStore<TestModel>();
        var repo = new TestRepo(store);
        repo.DataBaseStore.Should().BeSameAs(store);
    }

    [Fact]
    public void Constructor_RejectsUnrelatedStoreType()
    {
        // A store that is not an AsyncDataBaseBulkStore<SqLiteConnector, TModel> must still be rejected.
        var wrongStore = new Birko.Data.InMemory.Stores.AsyncInMemoryStore<TestModel>();
        Action act = () => new TestRepo(wrongStore);
        act.Should().Throw<ArgumentException>();
    }
}
