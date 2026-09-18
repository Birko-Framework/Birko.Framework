using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;

namespace Birko.Telemetry;

/// <summary>
/// Shared instrumentation helper that records metrics and traces for store operations.
/// </summary>
internal static class StoreInstrumentation
{
    internal static readonly Meter StoreMeter = new(BirkoTelemetryConventions.MeterName);
    internal static readonly ActivitySource StoreActivitySource = new(BirkoTelemetryConventions.ActivitySourceName);

    internal static readonly Histogram<double> OperationDuration =
        StoreMeter.CreateHistogram<double>(
            BirkoTelemetryConventions.OperationDurationMetric,
            unit: "ms",
            description: "Duration of store operations in milliseconds");

    internal static readonly Counter<long> OperationCount =
        StoreMeter.CreateCounter<long>(
            BirkoTelemetryConventions.OperationCountMetric,
            description: "Total number of store operations");

    internal static readonly Counter<long> OperationErrors =
        StoreMeter.CreateCounter<long>(
            BirkoTelemetryConventions.OperationErrorMetric,
            description: "Total number of store operation errors");

    /// <summary>
    /// Executes a synchronous void store operation with instrumentation.
    /// </summary>
    internal static void Execute(string storeType, string entityType, string operation, bool isBulk, Action action)
    {
        var tags = CreateTags(storeType, entityType, operation, isBulk);
        using var activity = StartActivity(operation, storeType, entityType, isBulk);
        var sw = Stopwatch.StartNew();
        try
        {
            action();
            sw.Stop();
            RecordSuccess(tags, sw.Elapsed.TotalMilliseconds, activity);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordError(tags, sw.Elapsed.TotalMilliseconds, activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Executes a synchronous store operation with a return value and instrumentation.
    /// </summary>
    internal static TResult Execute<TResult>(string storeType, string entityType, string operation, bool isBulk, Func<TResult> func)
    {
        var tags = CreateTags(storeType, entityType, operation, isBulk);
        using var activity = StartActivity(operation, storeType, entityType, isBulk);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = func();
            sw.Stop();
            RecordSuccess(tags, sw.Elapsed.TotalMilliseconds, activity);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordError(tags, sw.Elapsed.TotalMilliseconds, activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Executes an asynchronous void store operation with instrumentation.
    /// </summary>
    internal static async Task ExecuteAsync(string storeType, string entityType, string operation, bool isBulk, Func<Task> func)
    {
        var tags = CreateTags(storeType, entityType, operation, isBulk);
        using var activity = StartActivity(operation, storeType, entityType, isBulk);
        var sw = Stopwatch.StartNew();
        try
        {
            await func().ConfigureAwait(false);
            sw.Stop();
            RecordSuccess(tags, sw.Elapsed.TotalMilliseconds, activity);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordError(tags, sw.Elapsed.TotalMilliseconds, activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Executes an asynchronous store operation with a return value and instrumentation.
    /// </summary>
    internal static async Task<TResult> ExecuteAsync<TResult>(string storeType, string entityType, string operation, bool isBulk, Func<Task<TResult>> func)
    {
        var tags = CreateTags(storeType, entityType, operation, isBulk);
        using var activity = StartActivity(operation, storeType, entityType, isBulk);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await func().ConfigureAwait(false);
            sw.Stop();
            RecordSuccess(tags, sw.Elapsed.TotalMilliseconds, activity);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordError(tags, sw.Elapsed.TotalMilliseconds, activity, ex);
            throw;
        }
    }

    private static TagList CreateTags(string storeType, string entityType, string operation, bool isBulk)
    {
        return new TagList
        {
            { BirkoTelemetryConventions.StoreTypeTag, storeType },
            { BirkoTelemetryConventions.EntityTypeTag, entityType },
            { BirkoTelemetryConventions.OperationTag, operation },
            { BirkoTelemetryConventions.BulkTag, isBulk }
        };
    }

    private static Activity? StartActivity(string operation, string storeType, string entityType, bool isBulk)
    {
        var activity = StoreActivitySource.StartActivity($"{storeType}.{operation}");
        if (activity != null)
        {
            activity.SetTag(BirkoTelemetryConventions.StoreTypeTag, storeType);
            activity.SetTag(BirkoTelemetryConventions.EntityTypeTag, entityType);
            activity.SetTag(BirkoTelemetryConventions.OperationTag, operation);
            activity.SetTag(BirkoTelemetryConventions.BulkTag, isBulk);
        }
        return activity;
    }

    private static void RecordSuccess(TagList tags, double durationMs, Activity? activity)
    {
        OperationDuration.Record(durationMs, tags);
        OperationCount.Add(1, tags);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private static void RecordError(TagList tags, double durationMs, Activity? activity, Exception ex)
    {
        OperationDuration.Record(durationMs, tags);
        OperationCount.Add(1, tags);
        OperationErrors.Add(1, tags);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    }
}
