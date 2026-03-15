using Birko.Data.Stores;
using Birko.Telemetry;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Telemetry.Tests;

public class InstrumentedStoreWrapperTests : IDisposable
{
    private readonly MockStore _mockStore;
    private readonly InstrumentedStoreWrapper<MockStore, TestModel> _wrapper;
    private readonly MeterListener _listener;
    private readonly List<string> _recordedMetrics = new();

    public InstrumentedStoreWrapperTests()
    {
        _mockStore = new MockStore();
        _wrapper = new InstrumentedStoreWrapper<MockStore, TestModel>(_mockStore);

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
        var act = () => new InstrumentedStoreWrapper<MockStore, TestModel>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Read_ByGuid_DelegatesToInnerStore()
    {
        var guid = Guid.NewGuid();
        _mockStore.ReadResult = new TestModel { Guid = guid };

        var result = _wrapper.Read(guid);

        result.Should().NotBeNull();
        result!.Guid.Should().Be(guid);
        _mockStore.LastOperation.Should().Be("Read");
    }

    [Fact]
    public void Read_ByFilter_DelegatesToInnerStore()
    {
        _mockStore.ReadResult = new TestModel();
        var result = _wrapper.Read(x => true);
        result.Should().NotBeNull();
    }

    [Fact]
    public void Count_DelegatesToInnerStore()
    {
        _mockStore.CountResult = 42;
        var result = _wrapper.Count();
        result.Should().Be(42);
    }

    [Fact]
    public void Create_DelegatesToInnerStoreAndEmitsMetrics()
    {
        var model = new TestModel();
        _mockStore.CreateResult = Guid.NewGuid();

        var result = _wrapper.Create(model);

        result.Should().Be(_mockStore.CreateResult);
        _recordedMetrics.Should().Contain(BirkoTelemetryConventions.OperationDurationMetric);
        _recordedMetrics.Should().Contain(BirkoTelemetryConventions.OperationCountMetric);
    }

    [Fact]
    public void Update_DelegatesToInnerStore()
    {
        var model = new TestModel();
        _wrapper.Update(model);
        _mockStore.LastOperation.Should().Be("Update");
    }

    [Fact]
    public void Delete_DelegatesToInnerStore()
    {
        var model = new TestModel();
        _wrapper.Delete(model);
        _mockStore.LastOperation.Should().Be("Delete");
    }

    [Fact]
    public void Save_DelegatesToInnerStore()
    {
        var model = new TestModel();
        _mockStore.SaveResult = Guid.NewGuid();

        var result = _wrapper.Save(model);
        result.Should().Be(_mockStore.SaveResult);
    }

    [Fact]
    public void Init_DelegatesToInnerStore()
    {
        _wrapper.Init();
        _mockStore.LastOperation.Should().Be("Init");
    }

    [Fact]
    public void Destroy_DelegatesToInnerStore()
    {
        _wrapper.Destroy();
        _mockStore.LastOperation.Should().Be("Destroy");
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

    [Fact]
    public void GetInnerStoreAs_ReturnsTypedInnerStore()
    {
        _wrapper.GetInnerStoreAs<MockStore>().Should().BeSameAs(_mockStore);
        _wrapper.GetInnerStoreAs<string>().Should().BeNull();
    }

    #region Test Helpers

    public class TestModel : Data.Models.AbstractModel { }

    public class MockStore : IStore<TestModel>
    {
        public string? LastOperation { get; private set; }
        public TestModel? ReadResult { get; set; }
        public long CountResult { get; set; }
        public Guid CreateResult { get; set; } = Guid.NewGuid();
        public Guid SaveResult { get; set; } = Guid.NewGuid();

        public void Init() => LastOperation = "Init";
        public void Destroy() => LastOperation = "Destroy";
        public TestModel CreateInstance() => new();
        public long Count(Expression<Func<TestModel, bool>>? filter = null) { LastOperation = "Count"; return CountResult; }
        public TestModel? Read(Guid guid) { LastOperation = "Read"; return ReadResult; }
        public TestModel? Read(Expression<Func<TestModel, bool>>? filter = null) { LastOperation = "Read"; return ReadResult; }
        public Guid Create(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) { LastOperation = "Create"; return CreateResult; }
        public void Update(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => LastOperation = "Update";
        public void Delete(TestModel data) => LastOperation = "Delete";
        public Guid Save(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) { LastOperation = "Save"; return SaveResult; }
    }

    #endregion
}
