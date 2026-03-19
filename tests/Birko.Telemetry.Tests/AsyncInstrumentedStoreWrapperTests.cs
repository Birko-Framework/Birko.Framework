using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Telemetry;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Telemetry.Tests;

public class AsyncInstrumentedStoreWrapperTests : IDisposable
{
    private readonly MockAsyncStore _mockStore;
    private readonly AsyncInstrumentedStoreWrapper<MockAsyncStore, TestModel> _wrapper;
    private readonly MeterListener _listener;
    private readonly List<string> _recordedMetrics = new();

    public AsyncInstrumentedStoreWrapperTests()
    {
        _mockStore = new MockAsyncStore();
        _wrapper = new AsyncInstrumentedStoreWrapper<MockAsyncStore, TestModel>(_mockStore);

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == BirkoTelemetryConventions.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => _recordedMetrics.Add(instrument.Name));
        _listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => _recordedMetrics.Add(instrument.Name));
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new AsyncInstrumentedStoreWrapper<MockAsyncStore, TestModel>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ReadAsync_ByGuid_DelegatesToInnerStore()
    {
        var guid = Guid.NewGuid();
        _mockStore.ReadResult = new TestModel { Guid = guid };

        var result = await _wrapper.ReadAsync(guid);

        result.Should().NotBeNull();
        result!.Guid.Should().Be(guid);
        _mockStore.LastOperation.Should().Be("ReadAsync");
    }

    [Fact]
    public async Task ReadAsync_ByFilter_DelegatesToInnerStore()
    {
        _mockStore.ReadResult = new TestModel();
        var result = await _wrapper.ReadAsync(x => true);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CountAsync_DelegatesToInnerStore()
    {
        _mockStore.CountResult = 42;
        var result = await _wrapper.CountAsync();
        result.Should().Be(42);
    }

    [Fact]
    public async Task CreateAsync_DelegatesToInnerStoreAndEmitsMetrics()
    {
        var model = new TestModel();
        _mockStore.CreateResult = Guid.NewGuid();

        var result = await _wrapper.CreateAsync(model);

        result.Should().Be(_mockStore.CreateResult);
        _recordedMetrics.Should().Contain(BirkoTelemetryConventions.OperationDurationMetric);
        _recordedMetrics.Should().Contain(BirkoTelemetryConventions.OperationCountMetric);
    }

    [Fact]
    public async Task UpdateAsync_DelegatesToInnerStore()
    {
        var model = new TestModel();
        await _wrapper.UpdateAsync(model);
        _mockStore.LastOperation.Should().Be("UpdateAsync");
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToInnerStore()
    {
        var model = new TestModel();
        await _wrapper.DeleteAsync(model);
        _mockStore.LastOperation.Should().Be("DeleteAsync");
    }

    [Fact]
    public async Task SaveAsync_DelegatesToInnerStore()
    {
        var model = new TestModel();
        _mockStore.SaveResult = Guid.NewGuid();

        var result = await _wrapper.SaveAsync(model);
        result.Should().Be(_mockStore.SaveResult);
    }

    [Fact]
    public async Task InitAsync_DelegatesToInnerStore()
    {
        await _wrapper.InitAsync();
        _mockStore.LastOperation.Should().Be("InitAsync");
    }

    [Fact]
    public async Task DestroyAsync_DelegatesToInnerStore()
    {
        await _wrapper.DestroyAsync();
        _mockStore.LastOperation.Should().Be("DestroyAsync");
    }

    [Fact]
    public void CreateInstance_DelegatesToInnerStore()
    {
        var result = _wrapper.CreateInstance();
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        ((IStoreWrapper)_wrapper).GetInnerStore().Should().BeSameAs(_mockStore);
    }

    #region Test Helpers

    public class TestModel : Data.Models.AbstractModel { }

    public class MockAsyncStore : IAsyncStore<TestModel>
    {
        public string? LastOperation { get; private set; }
        public TestModel? ReadResult { get; set; }
        public long CountResult { get; set; }
        public Guid CreateResult { get; set; } = Guid.NewGuid();
        public Guid SaveResult { get; set; } = Guid.NewGuid();

        public Task InitAsync(CancellationToken ct = default) { LastOperation = "InitAsync"; return Task.CompletedTask; }
        public Task DestroyAsync(CancellationToken ct = default) { LastOperation = "DestroyAsync"; return Task.CompletedTask; }
        public TestModel CreateInstance() => new();
        public Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) { LastOperation = "CountAsync"; return Task.FromResult(CountResult); }
        public Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) { LastOperation = "ReadAsync"; return Task.FromResult(ReadResult); }
        public Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) { LastOperation = "ReadAsync"; return Task.FromResult(ReadResult); }
        public Task<Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default) { LastOperation = "CreateAsync"; return Task.FromResult(CreateResult); }
        public Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default) { LastOperation = "UpdateAsync"; return Task.CompletedTask; }
        public Task DeleteAsync(TestModel data, CancellationToken ct = default) { LastOperation = "DeleteAsync"; return Task.CompletedTask; }
        public Task<Guid> SaveAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default) { LastOperation = "SaveAsync"; return Task.FromResult(SaveResult); }
    }

    #endregion
}
