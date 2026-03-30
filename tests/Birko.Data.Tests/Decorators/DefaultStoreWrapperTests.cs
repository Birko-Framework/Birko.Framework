using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Stores;
using Birko.Configuration;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.Tests.Decorators;

public class DefaultStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel, IDefault
    {
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }

    private class TestBulkStore : AbstractBulkStore<TestModel>
    {
        private readonly Dictionary<Guid, TestModel> _data = new();

        public override long Count(Expression<Func<TestModel, bool>>? filter = null)
        {
            if (filter == null) return _data.Count;
            return _data.Values.AsQueryable().Count(filter);
        }

        public override TestModel? Read(Guid guid) => _data.GetValueOrDefault(guid);
        public override TestModel? Read(Expression<Func<TestModel, bool>>? filter = null) =>
            filter == null ? _data.Values.FirstOrDefault() : _data.Values.AsQueryable().FirstOrDefault(filter);
        public override IEnumerable<TestModel> Read() => _data.Values.ToList();

        public override IEnumerable<TestModel> Read(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null)
        {
            IEnumerable<TestModel> result = _data.Values;
            if (filter != null) result = result.AsQueryable().Where(filter);
            return result.ToList();
        }

        public override Guid Create(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return data.Guid.Value;
        }

        public override void Create(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            foreach (var item in data) Create(item, storeDelegate);
        }

        public override void Update(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
        }

        public override void Update(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            foreach (var item in data) Update(item, storeDelegate);
        }

        public override void Update(Expression<Func<TestModel, bool>> filter, Action<TestModel> updateAction)
        {
            var matches = _data.Values.AsQueryable().Where(filter).ToList();
            foreach (var item in matches) updateAction(item);
        }

        public override void Update(Expression<Func<TestModel, bool>> filter, PropertyUpdate<TestModel> updates) { }

        public override void Delete(TestModel data)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
        }

        public override void Delete(IEnumerable<TestModel> data)
        {
            foreach (var item in data) Delete(item);
        }

        public override void Delete(Expression<Func<TestModel, bool>> filter)
        {
            var toDelete = _data.Values.AsQueryable().Where(filter).ToList();
            foreach (var item in toDelete) Delete(item);
        }

        public override void Init() { }
        public override void Destroy() { }
        public override TestModel CreateInstance() => new();
        public override Guid Save(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            if (data.Guid == null || data.Guid == Guid.Empty) return Create(data, storeDelegate);
            Update(data, storeDelegate);
            return data.Guid.Value;
        }
    }

    #endregion

    #region Constructor

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new DefaultStoreWrapper<TestBulkStore, TestModel>(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    #endregion

    #region Create

    [Fact]
    public void Create_WithIsDefault_UnsetsOtherDefaults()
    {
        var store = new TestBulkStore();
        var wrapper = new DefaultStoreWrapper<TestBulkStore, TestModel>(store);

        var first = new TestModel { Name = "First", IsDefault = true };
        wrapper.Create(first);
        var second = new TestModel { Name = "Second", IsDefault = true };
        wrapper.Create(second);

        first.IsDefault.Should().BeFalse();
        second.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Create_WithoutIsDefault_LeavesExistingDefaults()
    {
        var store = new TestBulkStore();
        var wrapper = new DefaultStoreWrapper<TestBulkStore, TestModel>(store);

        var first = new TestModel { Name = "First", IsDefault = true };
        wrapper.Create(first);
        var second = new TestModel { Name = "Second", IsDefault = false };
        wrapper.Create(second);

        first.IsDefault.Should().BeTrue();
        second.IsDefault.Should().BeFalse();
    }

    #endregion

    #region Update

    [Fact]
    public void Update_WithIsDefault_UnsetsOtherDefaults()
    {
        var store = new TestBulkStore();
        var wrapper = new DefaultStoreWrapper<TestBulkStore, TestModel>(store);

        var first = new TestModel { Name = "First", IsDefault = true };
        wrapper.Create(first);
        var second = new TestModel { Name = "Second", IsDefault = false };
        wrapper.Create(second);

        second.IsDefault = true;
        wrapper.Update(second);

        first.IsDefault.Should().BeFalse();
        second.IsDefault.Should().BeTrue();
    }

    #endregion

    #region Save

    [Fact]
    public void Save_NewDefaultEntity_UnsetsOtherDefaults()
    {
        var store = new TestBulkStore();
        var wrapper = new DefaultStoreWrapper<TestBulkStore, TestModel>(store);

        var first = new TestModel { Name = "First", IsDefault = true };
        wrapper.Save(first);
        var second = new TestModel { Name = "Second", IsDefault = true };
        wrapper.Save(second);

        first.IsDefault.Should().BeFalse();
        second.IsDefault.Should().BeTrue();
    }

    #endregion

    #region Delete (passthrough)

    [Fact]
    public void Delete_DoesNotAffectDefaults()
    {
        var store = new TestBulkStore();
        var wrapper = new DefaultStoreWrapper<TestBulkStore, TestModel>(store);

        var first = new TestModel { Name = "First", IsDefault = true };
        wrapper.Create(first);
        var second = new TestModel { Name = "Second", IsDefault = false };
        wrapper.Create(second);

        wrapper.Delete(second);

        first.IsDefault.Should().BeTrue();
    }

    #endregion

    #region Read (passthrough)

    [Fact]
    public void Read_DelegatesToInnerStore()
    {
        var store = new TestBulkStore();
        var wrapper = new DefaultStoreWrapper<TestBulkStore, TestModel>(store);
        var model = new TestModel { Name = "Test" };
        wrapper.Create(model);

        wrapper.Read(model.Guid!.Value).Should().NotBeNull();
    }

    #endregion

    #region GetInnerStore

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var store = new TestBulkStore();
        var wrapper = new DefaultStoreWrapper<TestBulkStore, TestModel>(store);

        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    #endregion
}
