using Birko.Telemetry;
using FluentAssertions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Telemetry.Tests;

[Collection(StoreMeterCollection.Name)]
public class StoreInstrumentationTests : IDisposable
{
    private readonly MeterListener _listener;
    // TASK-657: thread-safe, and only this instance's measurements (its own entity tag), so neither another thread nor
    // another test can lose or satisfy an entry.
    private readonly string _entity = "T657-" + Guid.NewGuid().ToString("N");
    private readonly ConcurrentQueue<(string Name, double Value)> _recordedHistograms = new();
    private readonly ConcurrentQueue<(string Name, long Value)> _recordedCounters = new();

    private bool IsMine(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == BirkoTelemetryConventions.EntityTypeTag) return Equals(tag.Value, _entity);
        }
        return false;
    }

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
            if (IsMine(tags)) _recordedHistograms.Enqueue((instrument.Name, measurement));
        });
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (IsMine(tags)) _recordedCounters.Enqueue((instrument.Name, measurement));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    [Fact]
    public void Every_measurement_is_recorded_while_other_threads_publish()
    {
        // TASK-657: the old List lost 3-4% of 40 000 entries under 8 publishing threads (measured), silently.
        const int threads = 8, each = 2000;
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
        {
            for (var k = 0; k < each; k++)
            {
                try { StoreInstrumentation.Execute("TestStore", _entity, "Delete", false, () => throw new InvalidOperationException()); }
                catch (InvalidOperationException) { }
            }
        });

        _recordedCounters.Count(x => x.Name == BirkoTelemetryConventions.OperationErrorMetric).Should().Be(threads * each);
        _recordedCounters.Count(x => x.Name == BirkoTelemetryConventions.OperationCountMetric).Should().Be(threads * each);
    }

    [Fact]
    public void Execute_Void_RecordsDurationAndCount()
    {
        StoreInstrumentation.Execute("TestStore", _entity, "Create", false, () => { });

        _listener.RecordObservableInstruments();

        _recordedHistograms.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationDurationMetric);
        _recordedCounters.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationCountMetric);
        _recordedCounters.Should().NotContain(x => x.Name == BirkoTelemetryConventions.OperationErrorMetric);
    }

    [Fact]
    public void Execute_WithResult_ReturnsValueAndRecordsMetrics()
    {
        var result = StoreInstrumentation.Execute("TestStore", _entity, "Read", false, () => 42);

        _listener.RecordObservableInstruments();

        result.Should().Be(42);
        _recordedHistograms.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationDurationMetric);
        _recordedCounters.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationCountMetric);
    }

    [Fact]
    public void Execute_OnException_RecordsErrorAndRethrows()
    {
        var act = () => StoreInstrumentation.Execute("TestStore", _entity, "Delete", false, () =>
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
        await StoreInstrumentation.ExecuteAsync("TestStore", _entity, "Update", false, () => Task.CompletedTask);

        _listener.RecordObservableInstruments();

        _recordedHistograms.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationDurationMetric);
        _recordedCounters.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationCountMetric);
    }

    [Fact]
    public async Task ExecuteAsync_WithResult_ReturnsValueAndRecordsMetrics()
    {
        var result = await StoreInstrumentation.ExecuteAsync("TestStore", _entity, "Read", false, () => Task.FromResult(99));

        _listener.RecordObservableInstruments();

        result.Should().Be(99);
        _recordedHistograms.Should().Contain(x => x.Name == BirkoTelemetryConventions.OperationDurationMetric);
    }

    [Fact]
    public async Task ExecuteAsync_OnException_RecordsErrorAndRethrows()
    {
        var act = () => StoreInstrumentation.ExecuteAsync("TestStore", _entity, "Create", false,
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
        StoreInstrumentation.Execute("TestStore", _entity, "Read", false, () =>
        {
            captured = Activity.Current;
        });

        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("TestStore.Read");
    }
}
