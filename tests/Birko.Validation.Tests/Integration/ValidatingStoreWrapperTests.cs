using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Validation;
using Birko.Validation.Fluent;
using Birko.Validation.Integration;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Validation.Tests.Integration;

public class ValidatingStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel
    {
        public string Name { get; set; } = string.Empty;
    }

    private class TestValidator : AbstractValidator<TestModel>
    {
        public TestValidator()
        {
            RuleFor(x => x.Name).Required();
        }
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

    #endregion

    #region Constructor

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new ValidatingStoreWrapper<TestStore, TestModel>(null!, new TestValidator());
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    [Fact]
    public void Constructor_NullValidator_Throws()
    {
        var act = () => new ValidatingStoreWrapper<TestStore, TestModel>(new TestStore(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("validator");
    }

    #endregion

    #region Create

    [Fact]
    public void Create_ValidEntity_Succeeds()
    {
        var store = new TestStore();
        var wrapper = new ValidatingStoreWrapper<TestStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "Valid" };

        var guid = wrapper.Create(model);

        guid.Should().NotBeEmpty();
        store.Read(guid).Should().NotBeNull();
    }

    [Fact]
    public void Create_InvalidEntity_ThrowsValidationException()
    {
        var store = new TestStore();
        var wrapper = new ValidatingStoreWrapper<TestStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "" };

        var act = () => wrapper.Create(model);

        act.Should().Throw<ValidationException>();
    }

    #endregion

    #region Update

    [Fact]
    public void Update_InvalidEntity_ThrowsValidationException()
    {
        var store = new TestStore();
        var wrapper = new ValidatingStoreWrapper<TestStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "Valid" };
        wrapper.Create(model);
        model.Name = "";

        var act = () => wrapper.Update(model);

        act.Should().Throw<ValidationException>();
    }

    #endregion

    #region Save

    [Fact]
    public void Save_InvalidEntity_ThrowsValidationException()
    {
        var store = new TestStore();
        var wrapper = new ValidatingStoreWrapper<TestStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "" };

        var act = () => wrapper.Save(model);

        act.Should().Throw<ValidationException>();
    }

    #endregion

    #region Delete (passthrough)

    [Fact]
    public void Delete_DoesNotValidate()
    {
        var store = new TestStore();
        var wrapper = new ValidatingStoreWrapper<TestStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "Valid" };
        wrapper.Create(model);
        model.Name = ""; // make invalid

        var act = () => wrapper.Delete(model);

        act.Should().NotThrow();
    }

    #endregion

    #region Read (passthrough)

    [Fact]
    public void Read_DelegatesToInnerStore()
    {
        var store = new TestStore();
        var wrapper = new ValidatingStoreWrapper<TestStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "Test" };
        wrapper.Create(model);

        wrapper.Read(model.Guid!.Value).Should().NotBeNull();
    }

    #endregion

    #region GetInnerStore

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var store = new TestStore();
        var wrapper = new ValidatingStoreWrapper<TestStore, TestModel>(store, new TestValidator());

        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    #endregion
}
