using Birko.Telemetry;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Telemetry.Tests;

public class StoreInstrumentationTests : IDisposable
{
    private readonly MeterListener _listener;
    private readonly List<(string Name, double Value)> _recordedHistograms = new();
    private readonly List<(string Name, long Value)> _recordedCounters = new();

    public StoreInstrumentationTests()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == BirkoTelemetryConventions.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            _recordedHistograms.Add((instrument.Name, measurement));
        });
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _recordedCounters.Add((instrument.Name, measurement));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    [Fact]
    public void Execute_Void_RecordsDurationAndCount()
    {
        StoreInstrumentation.Execute("TestStore", "TestEntity", "Create", false, () => { });

        _listener.RecordObservableInstruments();

        _recordedHistograms.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationDurationMetric);
        _recordedCounters.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationCountMetric);
        _recordedCounters.Should().NotContain(x => x.Name == BirkoTelemetryConventions.OperationErrorMetric);
    }

    [Fact]
    public void Execute_WithResult_ReturnsValueAndRecordsMetrics()
    {
        var result = StoreInstrumentation.Execute("TestStore", "TestEntity", "Read", false, () => 42);

        _listener.RecordObservableInstruments();

        result.Should().Be(42);
        _recordedHistograms.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationDurationMetric);
        _recordedCounters.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationCountMetric);
    }

    [Fact]
    public void Execute_OnException_RecordsErrorAndRethrows()
    {
        var act = () => StoreInstrumentation.Execute("TestStore", "TestEntity", "Delete", false, () =>
        {
            throw new InvalidOperationException("test error");
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("test error");

        _listener.RecordObservableInstruments();

        _recordedCounters.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationErrorMetric);
    }

    [Fact]
    public async Task ExecuteAsync_Void_RecordsDurationAndCount()
    {
        await StoreInstrumentation.ExecuteAsync("TestStore", "TestEntity", "Update", false, () => Task.CompletedTask);

        _listener.RecordObservableInstruments();

        _recordedHistograms.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationDurationMetric);
        _recordedCounters.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationCountMetric);
    }

    [Fact]
    public async Task ExecuteAsync_WithResult_ReturnsValueAndRecordsMetrics()
    {
        var result = await StoreInstrumentation.ExecuteAsync("TestStore", "TestEntity", "Read", false, () => Task.FromResult(99));

        _listener.RecordObservableInstruments();

        result.Should().Be(99);
        _recordedHistograms.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationDurationMetric);
    }

    [Fact]
    public async Task ExecuteAsync_OnException_RecordsErrorAndRethrows()
    {
        var act = () => StoreInstrumentation.ExecuteAsync("TestStore", "TestEntity", "Create", false,
            () => Task.FromException(new InvalidOperationException("async error")));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("async error");

        _listener.RecordObservableInstruments();

        _recordedCounters.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationErrorMetric);
    }

    [Fact]
    public void Execute_CreatesActivity()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BirkoTelemetryConventions.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(activityListener);

        Activity? captured = null;
        StoreInstrumentation.Execute("TestStore", "TestEntity", "Read", false, () =>
        {
            captured = Activity.Current;
        });

        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("TestStore.Read");
    }
}
