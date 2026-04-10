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

public class AsyncValidatingBulkStoreWrapperTests
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

    private class TestAsyncBulkStore : AbstractAsyncBulkStore<TestModel>
    {
        private readonly Dictionary<Guid, TestModel> _data = new();

        public override Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) =>
            Task.FromResult<TestModel?>(_data.GetValueOrDefault(guid));

        protected override Task<TestModel?> ReadCoreAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
        {
            if (filter == null) return Task.FromResult<TestModel?>(_data.Values.FirstOrDefault());
            return Task.FromResult<TestModel?>(_data.Values.AsQueryable().FirstOrDefault(filter));
        }

        public override Task<IEnumerable<TestModel>> ReadAsync(CancellationToken ct = default) =>
            Task.FromResult<IEnumerable<TestModel>>(_data.Values.ToList());

        protected override Task<IEnumerable<TestModel>> ReadCoreAsync(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            IEnumerable<TestModel> result = _data.Values;
            if (filter != null) result = result.AsQueryable().Where(filter);
            return Task.FromResult(result);
        }

        protected override Task<long> CountCoreAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) =>
            Task.FromResult((long)_data.Count);

        protected override Task<Guid> CreateCoreAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return Task.FromResult(data.Guid.Value);
        }

        protected override Task CreateCoreAsync(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default)
        {
            foreach (var item in data)
            {
                item.Guid ??= Guid.NewGuid();
                _data[item.Guid.Value] = item;
            }
            return Task.CompletedTask;
        }

        protected override Task UpdateCoreAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
            return Task.CompletedTask;
        }

        protected override Task UpdateCoreAsync(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default)
        {
            foreach (var item in data)
                if (item.Guid.HasValue) _data[item.Guid.Value] = item;
            return Task.CompletedTask;
        }

        public override Task UpdateAsync(Expression<Func<TestModel, bool>> filter, Action<TestModel> updateAction, CancellationToken ct = default)
        {
            var matches = _data.Values.AsQueryable().Where(filter).ToList();
            foreach (var item in matches) updateAction(item);
            return Task.CompletedTask;
        }

        public override Task UpdateAsync(Expression<Func<TestModel, bool>> filter, PropertyUpdate<TestModel> updates, CancellationToken ct = default) =>
            Task.CompletedTask;

        protected override Task DeleteCoreAsync(TestModel data, CancellationToken ct = default)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
            return Task.CompletedTask;
        }

        protected override Task DeleteCoreAsync(IEnumerable<TestModel> data, CancellationToken ct = default)
        {
            foreach (var item in data)
                if (item.Guid.HasValue) _data.Remove(item.Guid.Value);
            return Task.CompletedTask;
        }

        public override Task DeleteAsync(Expression<Func<TestModel, bool>> filter, CancellationToken ct = default)
        {
            var toDelete = _data.Values.AsQueryable().Where(filter).ToList();
            foreach (var item in toDelete)
                if (item.Guid.HasValue) _data.Remove(item.Guid.Value);
            return Task.CompletedTask;
        }

        protected override Task InitCoreAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override TestModel CreateInstance() => new();
    }

    #endregion

    #region Batch Create

    [Fact]
    public async Task CreateAsync_Batch_AllValid_Succeeds()
    {
        var store = new TestAsyncBulkStore();
        var wrapper = new AsyncValidatingBulkStoreWrapper<TestAsyncBulkStore, TestModel>(store, new TestValidator());
        var batch = new List<TestModel>
        {
            new() { Name = "A" },
            new() { Name = "B" },
            new() { Name = "C" }
        };

        var act = () => wrapper.CreateAsync(batch);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateAsync_Batch_OneInvalid_ThrowsWithAllErrors()
    {
        var store = new TestAsyncBulkStore();
        var wrapper = new AsyncValidatingBulkStoreWrapper<TestAsyncBulkStore, TestModel>(store, new TestValidator());
        var batch = new List<TestModel>
        {
            new() { Name = "Valid" },
            new() { Name = "" },
            new() { Name = "Also Valid" }
        };

        var act = () => wrapper.CreateAsync(batch);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.ValidationResult.Errors.Should().NotBeEmpty();
    }

    #endregion

    #region Batch Update

    [Fact]
    public async Task UpdateAsync_Batch_OneInvalid_ThrowsWithAllErrors()
    {
        var store = new TestAsyncBulkStore();
        var wrapper = new AsyncValidatingBulkStoreWrapper<TestAsyncBulkStore, TestModel>(store, new TestValidator());
        var batch = new List<TestModel>
        {
            new() { Name = "" },
            new() { Name = "" }
        };

        var act = () => wrapper.UpdateAsync(batch);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.ValidationResult.Errors.Should().HaveCountGreaterOrEqualTo(2);
    }

    #endregion

    #region Batch Delete (passthrough)

    [Fact]
    public async Task DeleteAsync_Batch_DoesNotValidate()
    {
        var store = new TestAsyncBulkStore();
        var wrapper = new AsyncValidatingBulkStoreWrapper<TestAsyncBulkStore, TestModel>(store, new TestValidator());
        var model = new TestModel { Name = "Valid" };
        await wrapper.CreateAsync(model);
        model.Name = "";

        var act = () => wrapper.DeleteAsync(new[] { model });

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Read (passthrough)

    [Fact]
    public async Task ReadAsync_Bulk_DelegatesToInnerStore()
    {
        var store = new TestAsyncBulkStore();
        var wrapper = new AsyncValidatingBulkStoreWrapper<TestAsyncBulkStore, TestModel>(store, new TestValidator());
        await wrapper.CreateAsync(new TestModel { Name = "A" });
        await wrapper.CreateAsync(new TestModel { Name = "B" });

        var result = await wrapper.ReadAsync(CancellationToken.None);

        result.Should().HaveCount(2);
    }

    #endregion
}
