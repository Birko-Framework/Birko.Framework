using System;
using System.IO;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Extensions;
using Birko.Data.SQL.Repositories;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.ViewModel.Tests;

public class RepoModel : AbstractModel
{
    public string Name { get; set; } = string.Empty;
}

public class RepoViewModel : ILoadable<RepoModel>
{
    public Guid? Guid { get; set; }
    public string Name { get; set; } = string.Empty;
    public void LoadFrom(RepoModel data)
    {
        Guid = data.Guid;
        Name = data.Name;
    }
}

public class SyncRepo : DataBaseRepository<SqLiteConnector, RepoViewModel, RepoModel>
{
    public SyncRepo() : base() { }
    public SyncRepo(Birko.Data.Stores.IStore<RepoModel>? store) : base(store) { }
    protected override void MapToModel(RepoViewModel source, RepoModel target)
    {
        target.Guid = source.Guid;
        target.Name = source.Name;
    }
}

/// <summary>
/// CR-M152: the sync <see cref="DataBaseRepository{TConnector,TViewModel,TModel}"/> constructor guards,
/// the (unwrapping) <c>Connector</c> accessor, <c>AddOnInit</c>/<c>RemoveOnInit</c> delegation to the
/// inner connector, and the <c>ReadOne</c> extension. The async repo's constructor / generic-invariance
/// regression lives in <c>AsyncDataBaseRepositoryTests</c>. Runs against a real on-disk SQLite database.
/// </summary>
public class SyncDataBaseRepositoryTests : IDisposable
{
    private readonly string _root;

    public SyncDataBaseRepositoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqlvm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class RepoMapping : IModelMapping<RepoModel>
    {
        public void Configure(ModelMap<RepoModel> map)
        {
            map.ToTable("RepoModels").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private SQLiteStore<RepoModel> NewStore(string db = "repo.db")
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RepoMapping());
        registry.ApplyToDatabase();

        var store = new SQLiteStore<RepoModel>();
        store.SetSettings(new SqLiteSettings(_root, db));
        return store;
    }

    [Fact]
    public void Constructor_AcceptsConcreteStore_DoesNotThrow()
    {
        var store = new SQLiteStore<RepoModel>();
        Action act = () => new SyncRepo(store);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_RejectsUnrelatedStoreType()
    {
        var wrongStore = new Birko.Data.InMemory.Stores.InMemoryStore<RepoModel>();
        Action act = () => new SyncRepo(wrongStore);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DefaultConstructor_CreatesUsableRepo()
    {
        Action act = () => new SyncRepo();
        act.Should().NotThrow();
    }

    [Fact]
    public void Connector_ResolvesThroughStoreAfterSetSettings()
    {
        var repo = new SyncRepo(NewStore());
        repo.Connector.Should().NotBeNull();
        repo.Connector.Should().BeOfType<SqLiteConnector>();
    }

    [Fact]
    public void AddOnInit_And_RemoveOnInit_WireTheInnerConnector()
    {
        var repo = new SyncRepo(NewStore());
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

    // SH-H036: both tests below used to call the `ReadOne<TRepository, TConnector, TViewModel, TModel>`
    // EXTENSION, which read through `repository.Connector` and so skipped every store decorator. The
    // extension was removed rather than repaired (an extension cannot reach the protected decorated
    // `Store`), so these now exercise the instance `ReadOne` on AbstractViewModelRepository. The assertions
    // are unchanged — only the entry point moved — which is the point: the safe API produces the same
    // observable results for these cases, so nothing was traded away to close the leak.

    [Fact]
    public void ReadOne_ReturnsDefault_WhenNoStore()
    {
        // Was "WhenNoConnector": the short-circuit is now a null Store rather than a null Connector, which
        // is the same unusable-repository condition expressed at the layer that is actually decorator-aware.
        var repo = new SyncRepo();
        var result = repo.ReadOne();
        result.Should().BeNull();
    }

    [Fact]
    public void ReadOne_ReturnsMappedViewModel_FromStore()
    {
        var store = NewStore();
        var repo = new SyncRepo(store);
        repo.Connector!.CreateTable(new[] { typeof(RepoModel) });

        store.Create(new RepoModel { Name = "hello" });

        var vm = repo.ReadOne();
        vm.Should().NotBeNull();
        vm!.Name.Should().Be("hello");
        vm.Guid.Should().NotBeNull();
    }

    [Fact]
    public void ReadOne_WithAnOrdering_ReadsThroughTheStore_NotTheConnector()
    {
        // The overload that carries the defect's signature. It must reach the same rows as the unordered
        // form — through Store, so any decorator wrapping it still applies — and honour the ordering.
        var store = NewStore();
        var repo = new SyncRepo(store);
        repo.Connector!.CreateTable(new[] { typeof(RepoModel) });

        store.Create(new RepoModel { Name = "b" });
        store.Create(new RepoModel { Name = "a" });

        repo.ReadOne(null, OrderBy<RepoModel>.By(m => m.Name!))!.Name.Should().Be("a");
        repo.ReadOne(null, OrderBy<RepoModel>.ByDescending(m => m.Name!))!.Name.Should().Be("b");
    }
}
