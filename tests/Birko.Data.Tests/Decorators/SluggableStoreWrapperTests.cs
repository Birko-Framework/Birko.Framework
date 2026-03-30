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

public class SluggableStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel, ISluggable
    {
        public string Name { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? GetSlugSource() => Name;
    }

    private class TestStore : AbstractStore<TestModel>
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

        public override Guid Create(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return data.Guid.Value;
        }

        public override void Update(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
        }

        public override void Delete(TestModel data)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
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
        var act = () => new SluggableStoreWrapper<TestStore, TestModel>(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    #endregion

    #region Create

    [Fact]
    public void Create_SetsSlugFromSource()
    {
        var store = new TestStore();
        var wrapper = new SluggableStoreWrapper<TestStore, TestModel>(store);
        var model = new TestModel { Name = "Test Product" };

        wrapper.Create(model);

        model.Slug.Should().Be("test-product");
    }

    [Fact]
    public void Create_NormalizesSlug_LowercaseAndHyphens()
    {
        var store = new TestStore();
        var wrapper = new SluggableStoreWrapper<TestStore, TestModel>(store);
        var model = new TestModel { Name = "Hello World Test" };

        wrapper.Create(model);

        model.Slug.Should().Be("hello-world-test");
    }

    [Fact]
    public void Create_DuplicateSlug_AppendsSuffix()
    {
        var store = new TestStore();
        var wrapper = new SluggableStoreWrapper<TestStore, TestModel>(store);

        var first = new TestModel { Name = "Test" };
        wrapper.Create(first);
        var second = new TestModel { Name = "Test" };
        wrapper.Create(second);

        first.Slug.Should().Be("test");
        second.Slug.Should().Be("test-2");
    }

    #endregion

    #region Update

    [Fact]
    public void Update_UpdatesSlug()
    {
        var store = new TestStore();
        var wrapper = new SluggableStoreWrapper<TestStore, TestModel>(store);

        var model = new TestModel { Name = "Original" };
        wrapper.Create(model);
        model.Name = "Updated";
        model.Slug = null; // clear slug to regenerate
        wrapper.Update(model);

        model.Slug.Should().Be("updated");
    }

    #endregion

    #region Save

    [Fact]
    public void Save_NewEntity_GeneratesSlug()
    {
        var store = new TestStore();
        var wrapper = new SluggableStoreWrapper<TestStore, TestModel>(store);
        var model = new TestModel { Name = "New Item" };

        wrapper.Save(model);

        model.Slug.Should().Be("new-item");
    }

    #endregion

    #region GetInnerStore

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var store = new TestStore();
        var wrapper = new SluggableStoreWrapper<TestStore, TestModel>(store);

        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    #endregion
}
