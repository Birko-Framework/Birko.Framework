using Birko.Data.Models;
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

namespace Birko.Data.Tests.Stores
{
    /// <summary>
    /// Unit tests for IAsyncStore implementations.
    /// </summary>
    public class AsyncStoreTests
    {
        private class TestModel : AbstractModel
        {
            public TestModel() : base() { }

            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
        }

        private class TestAsyncStore : AbstractAsyncStore<TestModel>
        {
            private readonly Dictionary<Guid, TestModel> _data = new();

            public override Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
            {
                if (filter == null)
                {
                    return Task.FromResult<TestModel?>(_data.Values.FirstOrDefault());
                }
                var compiled = filter.Compile();
                return Task.FromResult<TestModel?>(_data.Values.FirstOrDefault(compiled));
            }

            public override Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
            {
                long count = _data.Count;
                return Task.FromResult(count);
            }

            public override Task<Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
            {
                if (data == null) return Task.FromResult(Guid.Empty);

                data.Guid = Guid.NewGuid();
                processDelegate?.Invoke(data);
                _data[data.Guid.Value] = data;
                return Task.FromResult(data.Guid.Value);
            }

            public override Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
            {
                if (data?.Guid != null && _data.ContainsKey(data.Guid.Value))
                {
                    processDelegate?.Invoke(data);
                    _data[data.Guid.Value] = data;
                }
                return Task.CompletedTask;
            }

            public override Task DeleteAsync(TestModel data, CancellationToken ct = default)
            {
                if (data?.Guid != null && _data.ContainsKey(data.Guid.Value))
                {
                    _data.Remove(data.Guid.Value);
                }
                return Task.CompletedTask;
            }

            public override Task InitAsync(CancellationToken ct = default)
            {
                return Task.CompletedTask;
            }

            public override Task DestroyAsync(CancellationToken ct = default)
            {
                _data.Clear();
                return Task.CompletedTask;
            }

            public override TestModel CreateInstance()
            {
                return new TestModel();
            }
        }

        [Fact]
        public async Task CreateAsync_ShouldAssignGuid()
        {
            // Arrange
            var store = new TestAsyncStore();
            var model = new TestModel();

            // Act
            await store.CreateAsync(model);

            // Assert
            model.Guid.Should().NotBeEmpty();
        }

        [Fact]
        public async Task CreateAsync_ShouldCallProcessDelegate()
        {
            // Arrange
            var store = new TestAsyncStore();
            var model = new TestModel { Name = "Test" };
            bool delegateCalled = false;

            // Act
            await store.CreateAsync(model, data =>
            {
                delegateCalled = true;
                data.Name = "Modified";
                return data;
            });

            // Assert
            delegateCalled.Should().BeTrue();
            model.Name.Should().Be("Modified");
        }

        [Fact]
        public async Task ReadAsync_ShouldReturnModel()
        {
            // Arrange
            var store = new TestAsyncStore();
            var model = new TestModel();
            await store.CreateAsync(model);

            // Act
            var result = await store.ReadAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(model);
        }

        [Fact]
        public async Task ReadAsync_WithFilter_ShouldReturnMatchingModel()
        {
            // Arrange
            var store = new TestAsyncStore();
            var model1 = new TestModel();
            var model2 = new TestModel();
            await store.CreateAsync(model1);
            await store.CreateAsync(model2);

            // Act
            var result = await store.ReadAsync(m => m.Guid == model1.Guid);

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(model1);
        }

        [Fact]
        public async Task UpdateAsync_ShouldUpdateModel()
        {
            // Arrange
            var store = new TestAsyncStore();
            var model = new TestModel();
            await store.CreateAsync(model);

            // Act
            model.Name = "Updated";
            await store.UpdateAsync(model);

            // Assert
            var result = await store.ReadAsync();
            result.Should().NotBeNull();
            result?.Name.Should().Be("Updated");
        }

        [Fact]
        public async Task DeleteAsync_ShouldRemoveModel()
        {
            // Arrange
            var store = new TestAsyncStore();
            var model = new TestModel();
            await store.CreateAsync(model);

            // Act
            await store.DeleteAsync(model);

            // Assert
            var result = await store.ReadAsync();
            result.Should().BeNull();
        }

        [Fact]
        public async Task CountAsync_ShouldReturnCorrectCount()
        {
            // Arrange
            var store = new TestAsyncStore();
            await store.CreateAsync(new TestModel());
            await store.CreateAsync(new TestModel());
            await store.CreateAsync(new TestModel());

            // Act
            var count = await store.CountAsync();

            // Assert
            count.Should().Be(3);
        }

        [Fact]
        public async Task SaveAsync_ShouldCreateWhenGuidIsEmpty()
        {
            // Arrange
            var store = new TestAsyncStore();
            var model = new TestModel();

            // Act
            var guid = await store.SaveAsync(model);

            // Assert
            guid.Should().NotBeEmpty();
            model.Guid.Should().Be(guid);
        }

        [Fact]
        public async Task SaveAsync_ShouldUpdateWhenGuidIsNotEmpty()
        {
            // Arrange
            var store = new TestAsyncStore();
            var model = new TestModel();
            await store.CreateAsync(model);
            model.Name = "Updated";

            // Act
            await store.SaveAsync(model);

            // Assert
            var result = await store.ReadAsync();
            result?.Name.Should().Be("Updated");
        }

        [Fact]
        public async Task InitAsync_ShouldCompleteSuccessfully()
        {
            // Arrange
            var store = new TestAsyncStore();

            // Act & Assert
            await store.Invoking(s => s.InitAsync(default))
                .Should().NotThrowAsync();
        }

        [Fact]
        public async Task DestroyAsync_ShouldClearAllData()
        {
            // Arrange
            var store = new TestAsyncStore();
            await store.CreateAsync(new TestModel());
            await store.CreateAsync(new TestModel());

            // Act
            await store.DestroyAsync();

            // Assert
            var count = await store.CountAsync();
            count.Should().Be(0);
        }

        [Fact]
        public async Task CreateInstance_ShouldReturnType()
        {
            // Arrange
            var store = new TestAsyncStore();

            // Act
            var instance = store.CreateInstance();

            // Assert
            instance.Should().NotBeNull();
            instance.Should().BeOfType<TestModel>();
        }

        [Fact]
        public async Task Operations_ShouldRespectCancellationToken()
        {
            // Arrange
            var store = new TestAsyncStore();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var model = new TestModel();

            // Act & Assert
            await store.Invoking(s => s.CreateAsync(model, null, cts.Token))
                .Should().NotThrowAsync<TaskCanceledException>();

            await store.Invoking(s => s.ReadAsync(null, cts.Token))
                .Should().NotThrowAsync<TaskCanceledException>();
        }
    }
}
