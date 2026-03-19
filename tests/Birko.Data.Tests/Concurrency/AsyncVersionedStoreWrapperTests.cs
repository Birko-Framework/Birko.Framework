using Birko.Data.Models;
using Birko.Data.Patterns.Concurrency;
using Birko.Data.Stores;
using Birko.Configuration;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Tests.Concurrency;

public class AsyncVersionedStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel, IVersioned
    {
        public string Name { get; set; } = string.Empty;
        public long Version { get; set; }
    }

    private class TestAsyncStore : AbstractAsyncStore<TestModel>
    {
        private readonly Dictionary<Guid, TestModel> _data = new();

        public override Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
        {
            long count = filter == null ? _data.Count : _data.Values.AsQueryable().Count(filter);
            return Task.FromResult(count);
        }

        public override Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default)
            => Task.FromResult(_data.GetValueOrDefault(guid));

        public override Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
            => Task.FromResult(filter == null ? _data.Values.FirstOrDefault() : _data.Values.AsQueryable().FirstOrDefault(filter));

        public override Task<Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return Task.FromResult(data.Guid.Value);
        }

        public override Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
            return Task.CompletedTask;
        }

        public override Task DeleteAsync(TestModel data, CancellationToken ct = default)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
            return Task.CompletedTask;
        }

        public override Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override TestModel CreateInstance() => new();
        public override async Task<Guid> SaveAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (data.Guid == null || data.Guid == Guid.Empty) return await CreateAsync(data, processDelegate, ct);
            await UpdateAsync(data, processDelegate, ct);
            return data.Guid.Value;
        }
    }

    #endregion

    [Fact]
    public async Task CreateAsync_SetsVersionToOne()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncVersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);

        model.Version.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_IncrementsVersion()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncVersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);
        model.Version.Should().Be(1);

        await wrapper.UpdateAsync(model);
        model.Version.Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_StaleVersion_ThrowsConcurrentUpdateException()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncVersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);
        // model.Version == 1

        // Simulate another process updating via the wrapper
        var otherProcess = new TestModel { Guid = model.Guid, Name = "Updated by other", Version = 1 };
        await wrapper.UpdateAsync(otherProcess);
        // Store now has Version == 2

        // Our model still has Version == 1 (stale)
        model.Version = 1;
        var act = () => wrapper.UpdateAsync(model);

        await act.Should().ThrowAsync<ConcurrentUpdateException>()
            .Where(e => e.ExpectedVersion == 1);
    }

    [Fact]
    public async Task SaveAsync_NewEntity_SetsVersionToOne()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncVersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        await wrapper.SaveAsync(model);

        model.Version.Should().Be(1);
    }

    [Fact]
    public async Task SaveAsync_ExistingEntity_IncrementsVersion()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncVersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);
        await wrapper.SaveAsync(model);

        model.Version.Should().Be(2);
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new AsyncVersionedStoreWrapper<TestModel>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ReadAsync_DelegatesToInnerStore()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncVersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);
        var read = await wrapper.ReadAsync(model.Guid!.Value);

        read.Should().NotBeNull();
        read!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToInnerStore()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncVersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);
        await wrapper.DeleteAsync(model);

        (await wrapper.ReadAsync(model.Guid!.Value)).Should().BeNull();
    }

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncVersionedStoreWrapper<TestModel>(store);

        wrapper.GetInnerStore().Should().BeSameAs(store);
    }
}
