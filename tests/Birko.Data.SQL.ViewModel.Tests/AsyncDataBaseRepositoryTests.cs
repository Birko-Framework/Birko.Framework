using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Repositories;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using System;
using System.IO;
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

    private sealed class TestMapping : IModelMapping<TestModel>
    {
        public void Configure(ModelMap<TestModel> map)
        {
            map.ToTable("TestModels").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    // CR-L199: the async repo now exposes AddOnInit/RemoveOnInit for parity with the sync repo, delegating
    // to the (unwrapping) DataBaseStore's connector.
    [Fact]
    public void AddOnInit_And_RemoveOnInit_WireTheInnerConnector()
    {
        var root = Path.Combine(Path.GetTempPath(), $"birko-sqlvm-async-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var registry = new ModelMapRegistry();
            registry.Register(new TestMapping());
            registry.ApplyToDatabase();

            var store = new AsyncSQLiteStore<TestModel>();
            store.SetSettings(new SqLiteSettings(root, "async-oninit.db"));
            var repo = new TestRepo(store);

            bool fired = false;
            InitConnector handler = _ => fired = true;

            repo.AddOnInit(handler);
            repo.Connector!.DoInit();
            fired.Should().BeTrue("AddOnInit must register the handler on the inner connector's OnInit");

            fired = false;
            repo.RemoveOnInit(handler);
            repo.Connector!.DoInit();
            fired.Should().BeFalse("RemoveOnInit must unregister the handler");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
