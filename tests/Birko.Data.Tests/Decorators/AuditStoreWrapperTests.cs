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
using Xunit;

namespace Birko.Data.Tests.Decorators;

public class AuditStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel, IAuditable
    {
        public string Name { get; set; } = string.Empty;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
    }

    private class TestStore : AbstractStore<TestModel>
    {
        private readonly Dictionary<Guid, TestModel> _data = new();

        protected override long CountCore(Expression<Func<TestModel, bool>>? filter = null)
        {
            if (filter == null) return _data.Count;
            return _data.Values.AsQueryable().Count(filter);
        }

        public override TestModel? Read(Guid guid) => _data.GetValueOrDefault(guid);
        protected override TestModel? ReadCore(Expression<Func<TestModel, bool>>? filter = null) =>
            filter == null ? _data.Values.FirstOrDefault() : _data.Values.AsQueryable().FirstOrDefault(filter);

        protected override Guid CreateCore(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return data.Guid.Value;
        }

        protected override void UpdateCore(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
        }

        protected override void DeleteCore(TestModel data)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
        }

        protected override void InitCore() { }
        public override void Destroy() { }
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
        var context = new TestAuditContext { CurrentUserId = UserId };
        var act = () => new AuditStoreWrapper<TestStore, TestModel>(null!, context);
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        var store = new TestStore();
        var act = () => new AuditStoreWrapper<TestStore, TestModel>(store, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("auditContext");
    }

    #endregion

    #region Create

    [Fact]
    public void Create_SetsBothCreatedByAndUpdatedBy()
    {
        var context = new TestAuditContext { CurrentUserId = UserId };
        var store = new TestStore();
        var wrapper = new AuditStoreWrapper<TestStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);

        model.CreatedBy.Should().Be(UserId);
        model.UpdatedBy.Should().Be(UserId);
    }

    [Fact]
    public void Create_NullUser_SetsNullAuditFields()
    {
        var context = new TestAuditContext { CurrentUserId = null };
        var store = new TestStore();
        var wrapper = new AuditStoreWrapper<TestStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);

        model.CreatedBy.Should().BeNull();
        model.UpdatedBy.Should().BeNull();
    }

    #endregion

    #region Update

    [Fact]
    public void Update_SetsUpdatedBy_PreservesCreatedBy()
    {
        var originalUser = Guid.NewGuid();
        var updatingUser = Guid.NewGuid();
        var context = new TestAuditContext { CurrentUserId = originalUser };
        var store = new TestStore();
        var wrapper = new AuditStoreWrapper<TestStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);

        context.CurrentUserId = updatingUser;
        wrapper.Update(model);

        model.CreatedBy.Should().Be(originalUser);
        model.UpdatedBy.Should().Be(updatingUser);
    }

    #endregion

    #region Save

    [Fact]
    public void Save_NewEntity_SetsBothFields()
    {
        var context = new TestAuditContext { CurrentUserId = UserId };
        var store = new TestStore();
        var wrapper = new AuditStoreWrapper<TestStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        wrapper.Save(model);

        model.CreatedBy.Should().Be(UserId);
        model.UpdatedBy.Should().Be(UserId);
    }

    [Fact]
    public void Save_ExistingEntity_OnlySetsUpdatedBy()
    {
        var originalUser = Guid.NewGuid();
        var updatingUser = Guid.NewGuid();
        var context = new TestAuditContext { CurrentUserId = originalUser };
        var store = new TestStore();
        var wrapper = new AuditStoreWrapper<TestStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);

        context.CurrentUserId = updatingUser;
        wrapper.Save(model);

        model.CreatedBy.Should().Be(originalUser);
        model.UpdatedBy.Should().Be(updatingUser);
    }

    #endregion

    #region Delegation

    [Fact]
    public void Read_DelegatesToInnerStore()
    {
        var context = new TestAuditContext { CurrentUserId = UserId };
        var store = new TestStore();
        var wrapper = new AuditStoreWrapper<TestStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        var read = wrapper.Read(model.Guid!.Value);

        read.Should().NotBeNull();
        read!.Name.Should().Be("Test");
    }

    [Fact]
    public void Delete_DelegatesToInnerStore()
    {
        var context = new TestAuditContext { CurrentUserId = UserId };
        var store = new TestStore();
        var wrapper = new AuditStoreWrapper<TestStore, TestModel>(store, context);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        wrapper.Delete(model);

        wrapper.Read(model.Guid!.Value).Should().BeNull();
    }

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var context = new TestAuditContext { CurrentUserId = UserId };
        var store = new TestStore();
        var wrapper = new AuditStoreWrapper<TestStore, TestModel>(store, context);

        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    #endregion
}
