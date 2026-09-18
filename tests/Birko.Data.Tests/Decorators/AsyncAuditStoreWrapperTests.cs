using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
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

namespace Birko.Data.Tests.Decorators;

public class AsyncAuditStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel, IAuditable
    {
        public string Name { get; set; } = string.Empty;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
    }

    private class TestAsyncStore : AbstractAsyncStore<TestModel>
    {
        private readonly Dictionary<Guid, TestModel> _data = new();

        public override Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) =>
            Task.FromResult<TestModel?>(_data.GetValueOrDefault(guid));

        protected override Task<TestModel?> ReadCoreAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
        {
            if (filter == null) return Task.FromResult<TestModel?>(_data.Values.FirstOrDefault());
            return Task.FromResult<TestModel?>(_data.Values.AsQueryable().FirstOrDefault(filter));
        }

        protected override Task<long> CountCoreAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) =>
            Task.FromResult((long)_data.Count);

        protected override Task<Guid> CreateCoreAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return Task.FromResult(data.Guid.Value);
        }

        protected override Task UpdateCoreAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
            return Task.CompletedTask;
        }

        protected override Task DeleteCoreAsync(TestModel data, CancellationToken ct = default)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
            return Task.CompletedTask;
        }

        protected override Task InitCoreAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override TestModel CreateInstance() => new();
    }

    private class TestAuditContext : IAuditContext
    {
        public Guid? CurrentUserId { get; set; }
    }

    private static readonly Guid UserId = Guid.NewGuid();

    #endregion

    #region Constructor

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new AsyncAuditStoreWrapper<TestAsyncStore, TestModel>(null!, new TestAuditContext());
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        var act = () => new AsyncAuditStoreWrapper<TestAsyncStore, TestModel>(new TestAsyncStore(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("auditContext");
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_SetsBothCreatedByAndUpdatedBy()
    {
        var store = new TestAsyncStore();
        var context = new TestAuditContext { CurrentUserId = UserId };
        var wrapper = new AsyncAuditStoreWrapper<TestAsyncStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);

        model.CreatedBy.Should().Be(UserId);
        model.UpdatedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task CreateAsync_NullUser_SetsNullAuditFields()
    {
        var store = new TestAsyncStore();
        var context = new TestAuditContext { CurrentUserId = null };
        var wrapper = new AsyncAuditStoreWrapper<TestAsyncStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);

        model.CreatedBy.Should().BeNull();
        model.UpdatedBy.Should().BeNull();
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_SetsUpdatedBy_PreservesCreatedBy()
    {
        var store = new TestAsyncStore();
        var creatorId = Guid.NewGuid();
        var updaterId = Guid.NewGuid();
        var context = new TestAuditContext { CurrentUserId = creatorId };
        var wrapper = new AsyncAuditStoreWrapper<TestAsyncStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);
        context.CurrentUserId = updaterId;
        await wrapper.UpdateAsync(model);

        model.CreatedBy.Should().Be(creatorId);
        model.UpdatedBy.Should().Be(updaterId);
    }

    #endregion

    #region SaveAsync

    [Fact]
    public async Task SaveAsync_NewEntity_SetsBothFields()
    {
        var store = new TestAsyncStore();
        var context = new TestAuditContext { CurrentUserId = UserId };
        var wrapper = new AsyncAuditStoreWrapper<TestAsyncStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        await wrapper.SaveAsync(model);

        model.CreatedBy.Should().Be(UserId);
        model.UpdatedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task SaveAsync_ExistingEntity_OnlySetsUpdatedBy()
    {
        var store = new TestAsyncStore();
        var creatorId = Guid.NewGuid();
        var updaterId = Guid.NewGuid();
        var context = new TestAuditContext { CurrentUserId = creatorId };
        var wrapper = new AsyncAuditStoreWrapper<TestAsyncStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        await wrapper.SaveAsync(model); // new entity
        context.CurrentUserId = updaterId;
        await wrapper.SaveAsync(model); // existing entity

        model.CreatedBy.Should().Be(creatorId);
        model.UpdatedBy.Should().Be(updaterId);
    }

    #endregion

    #region ReadAsync / DeleteAsync (passthrough)

    [Fact]
    public async Task ReadAsync_DelegatesToInnerStore()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncAuditStoreWrapper<TestAsyncStore, TestModel>(store, new TestAuditContext { CurrentUserId = UserId });
        var model = new TestModel { Name = "Test" };
        await wrapper.CreateAsync(model);

        (await wrapper.ReadAsync(model.Guid!.Value)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToInnerStore()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncAuditStoreWrapper<TestAsyncStore, TestModel>(store, new TestAuditContext { CurrentUserId = UserId });
        var model = new TestModel { Name = "Test" };
        await wrapper.CreateAsync(model);

        await wrapper.DeleteAsync(model);

        (await store.ReadAsync(model.Guid!.Value)).Should().BeNull();
    }

    #endregion

    #region GetInnerStore

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncAuditStoreWrapper<TestAsyncStore, TestModel>(store, new TestAuditContext());

        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    #endregion
}
