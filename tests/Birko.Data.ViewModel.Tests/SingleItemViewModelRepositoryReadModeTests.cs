using System;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ViewModel.Tests;

/// <summary>
/// CR-L239: the single-item ViewModel repositories (sync <see cref="AbstractViewModelRepository{TViewModel, TModel}"/>
/// and async <see cref="AbstractAsyncViewModelRepository{TViewModel, TModel}"/>) guarded their write
/// methods with <c>throw new AccessViolationException</c> — a CLR corrupted-state exception (SEH) that
/// host policy (<c>legacyCorruptedStateExceptionsPolicy</c>) can make uncatchable by user code. The
/// guards now throw <see cref="InvalidOperationException"/>. These are the audit's primary sites; the
/// bulk siblings are pinned by <see cref="BulkViewModelRepositoryReadModeTests"/>.
/// </summary>
public class SingleItemViewModelRepositoryReadModeTests
{
    public class Model : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class Vm : ILoadable<Model>
    {
        public string? Name { get; set; }
        public void LoadFrom(Model data) => Name = data.Name;
    }

    private sealed class SyncRepo : AbstractViewModelRepository<Vm, Model>
    {
        public SyncRepo(IStore<Model> store) : base(store) { }
        protected override void MapToModel(Vm source, Model target) => target.Name = source.Name;
    }

    private sealed class AsyncRepo : AbstractAsyncViewModelRepository<Vm, Model>
    {
        public AsyncRepo(IAsyncStore<Model> store) : base(store) { }
        protected override void MapToModel(Vm source, Model target) => target.Name = source.Name;
    }

    // ---- sync single-item guards ----

    [Fact]
    public void ReadMode_blocks_sync_single_item_writes_with_InvalidOperationException()
    {
        var repo = new SyncRepo(new InMemoryStore<Model>()) { ReadMode = true };
        ((Action)(() => repo.Create(new Vm { Name = "x" }))).Should().Throw<InvalidOperationException>();
        ((Action)(() => repo.Update(new Vm { Name = "x" }))).Should().Throw<InvalidOperationException>();
        ((Action)(() => repo.Delete(new Vm { Name = "x" }))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Sync_single_item_create_still_works_when_not_in_read_mode()
    {
        var store = new InMemoryStore<Model>();
        var repo = new SyncRepo(store);
        repo.Create(new Vm { Name = "ok" });
        repo.Read().Should().NotBeNull();
    }

    // ---- async single-item guards ----

    [Fact]
    public async Task ReadMode_blocks_async_single_item_writes_with_InvalidOperationException()
    {
        var repo = new AsyncRepo(new AsyncInMemoryStore<Model>()) { ReadMode = true };
        await FluentActions.Awaiting(() => repo.CreateAsync(new Vm { Name = "x" })).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => repo.UpdateAsync(new Vm { Name = "x" })).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => repo.DeleteAsync(new Vm { Name = "x" })).Should().ThrowAsync<InvalidOperationException>();
    }
}
