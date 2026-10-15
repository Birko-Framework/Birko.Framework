using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ViewModel.Tests;

/// <summary>TASK-162, the medium findings in <c>repository-contract</c> that live in the ViewModel repositories.</summary>
public class ViewModelRepositoryTriageTests
{
    public class Model : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class Vm : ILoadable<Model>
    {
        public Guid? Guid { get; set; }
        public string? Name { get; set; }
        public void LoadFrom(Model data) { Guid = data.Guid; Name = data.Name; }
    }

    private sealed class Repo : AbstractBulkViewModelRepository<Vm, Model>
    {
        public Repo(IBulkStore<Model>? store) : base(store) { }
        public int Tracked => _modelHash.Count;
        protected override void MapToModel(Vm source, Model target) { target.Guid = source.Guid; target.Name = source.Name; }
    }

    private sealed class AsyncRepo : AbstractAsyncBulkViewModelRepository<Vm, Model>
    {
        public bool HookUsed;
        public AsyncRepo(IAsyncStore<Model>? store) : base(store) { }
        protected override void MapToModel(Vm source, Model target) { target.Guid = source.Guid; target.Name = source.Name; }
        public override Model LoadModelInstance(Vm model) { HookUsed = true; return base.LoadModelInstance(model); }
    }

    /// <summary>Records the order a read was asked for.</summary>
    private sealed class RecordingStore : InMemoryStore<Model>
    {
        public OrderBy<Model>? LastOrder;

        public override IEnumerable<Model> Read(Expression<Func<Model, bool>>? filter = null, OrderBy<Model>? orderBy = null, int? limit = null, int? offset = null)
        {
            LastOrder = orderBy;
            return base.Read(filter, orderBy, limit, offset);
        }
    }

    /// <summary>SH-M207: a deleted model stayed tracked for the repository's lifetime; nothing pruned the hash table.</summary>
    [Fact]
    public void Deleting_a_view_model_stops_tracking_it()
    {
        var repo = new Repo(new InMemoryStore<Model>());
        repo.Create(new Vm { Name = "a" });
        var read = repo.Read().Single();
        repo.Tracked.Should().Be(1);

        repo.Delete(read);

        repo.Tracked.Should().Be(0);
    }

    /// <summary>SH-M206 (pin): one repository serves concurrent readers; the hash table must survive them.</summary>
    [Fact]
    public void Concurrent_reads_on_one_repository_track_every_row_once()
    {
        var repo = new Repo(new InMemoryStore<Model>());
        for (var i = 0; i < 50; i++) repo.Create(new Vm { Name = "n" + i });

        Parallel.For(0, 32, _ => repo.Read().ToList());

        repo.Tracked.Should().Be(50);
    }

    /// <summary>SH-M214: a page read sent no order, so consecutive pages could repeat and skip rows.</summary>
    [Fact]
    public void A_page_read_is_ordered_and_an_unpaged_read_is_left_alone()
    {
        var store = new RecordingStore();
        var repo = new Repo(store);
        repo.Create(new Vm { Name = "a" });

        repo.Read(null, 10, 0).ToList();
        store.LastOrder!.Fields.Select(f => f.PropertyName).Should().Equal(nameof(Model.Guid));

        repo.Read().ToList();
        store.LastOrder.Should().BeNull();
    }

    /// <summary>SH-M215: the wrong-store check ran on the first MoveNext, not when Read was called.</summary>
    [Fact]
    public void A_read_without_a_bulk_store_throws_when_called_not_when_enumerated()
    {
        var repo = new Repo(null);
        var act = () => { repo.Read(); };
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>SH-M216: Delete had no null guard where Create and Update return early.</summary>
    [Fact]
    public void Null_data_is_a_no_op_on_every_write_like_Create_and_Update()
    {
        var repo = new Repo(new InMemoryStore<Model>());
        repo.Invoking(r => r.Delete((Vm)null!)).Should().NotThrow();
        repo.Invoking(r => r.Create((IEnumerable<Vm>)null!)).Should().NotThrow();
        repo.Invoking(r => r.Update((IEnumerable<Vm>)null!)).Should().NotThrow();
        repo.Invoking(r => r.Delete((IEnumerable<Vm>)null!)).Should().NotThrow();
    }

    /// <summary>SH-M220: the async single delete inlined the mapping and skipped a LoadModelInstance override.</summary>
    [Fact]
    public async Task The_async_delete_goes_through_LoadModelInstance()
    {
        var store = new AsyncInMemoryStore<Model>();
        var repo = new AsyncRepo(store);
        var guid = await store.CreateAsync(new Model { Name = "x" });
        repo.HookUsed = false;

        await repo.DeleteAsync(new Vm { Guid = guid, Name = "x" });

        repo.HookUsed.Should().BeTrue();
    }

    /// <summary>SH-M219 on the ViewModel side: a repository with no store answered as if it had worked.</summary>
    [Fact]
    public async Task A_view_model_repository_without_a_store_refuses()
    {
        var repo = new AsyncRepo(null);
        await repo.Invoking(r => r.CountAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*no store*");
        await repo.Invoking(r => r.CreateAsync(new Vm { Name = "x" })).Should().ThrowAsync<InvalidOperationException>();
    }
}
