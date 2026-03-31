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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Validation.Tests.Integration;

public class AsyncValidatingStoreWrapperTests
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

    private class TestAsyncStore : AbstractAsyncStore<TestModel>
    {
        private readonly Dictionary<Guid, TestModel> _data = new();

        public override Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) =>
            Task.FromResult<TestModel?>(_data.GetValueOrDefault(guid));

        public override Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
        {
            if (filter == null) return Task.FromResult<TestModel?>(_data.Values.FirstOrDefault());
            return Task.FromResult<TestModel?>(_data.Values.AsQueryable().FirstOrDefault(filter));
        }

        public override Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) =>
            Task.FromResult((long)_data.Count);

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
    }

    #endregion

    #region Constructor

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new AsyncValidatingStoreWrapper<TestAsyncStore, TestModel>(null!, new TestValidator());
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    [Fact]
    public void Constructor_NullValidator_Throws()
    {
        var act = () => new AsyncValidatingStoreWrapper<TestAsyncStore, TestModel>(new TestAsyncStore(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("validator");
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_ValidEntity_Succeeds()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncValidatingStoreWrapper<TestAsyncStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "Valid" };

        var guid = await wrapper.CreateAsync(model);

        guid.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_InvalidEntity_ThrowsValidationException()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncValidatingStoreWrapper<TestAsyncStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "" };

        var act = () => wrapper.CreateAsync(model);

        await act.Should().ThrowAsync<ValidationException>();
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_InvalidEntity_ThrowsValidationException()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncValidatingStoreWrapper<TestAsyncStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "Valid" };
        await wrapper.CreateAsync(model);
        model.Name = "";

        var act = () => wrapper.UpdateAsync(model);

        await act.Should().ThrowAsync<ValidationException>();
    }

    #endregion

    #region SaveAsync

    [Fact]
    public async Task SaveAsync_InvalidEntity_ThrowsValidationException()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncValidatingStoreWrapper<TestAsyncStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "" };

        var act = () => wrapper.SaveAsync(model);

        await act.Should().ThrowAsync<ValidationException>();
    }

    #endregion

    #region DeleteAsync (passthrough)

    [Fact]
    public async Task DeleteAsync_DoesNotValidate()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncValidatingStoreWrapper<TestAsyncStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "Valid" };
        await wrapper.CreateAsync(model);
        model.Name = "";

        var act = () => wrapper.DeleteAsync(model);

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region ReadAsync (passthrough)

    [Fact]
    public async Task ReadAsync_DelegatesToInnerStore()
    {
        var store = new TestAsyncStore();
        var wrapper = new AsyncValidatingStoreWrapper<TestAsyncStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "Test" };
        await wrapper.CreateAsync(model);

        var result = await wrapper.ReadAsync(model.Guid!.Value);

        result.Should().NotBeNull();
    }

    #endregion
}
