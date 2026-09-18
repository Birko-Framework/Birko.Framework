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

    // Backed by Birko.Data.InMemory's AsyncInMemoryStore<T> — no per-test overrides needed.
    private class TestAsyncBulkStore : Birko.Data.InMemory.Stores.AsyncInMemoryStore<TestModel>
    {
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
