using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Telemetry;

namespace Birko.Framework.Examples.Telemetry
{
    /// <summary>
    /// Examples demonstrating the Birko.Telemetry framework:
    /// store instrumentation, metrics collection, distributed tracing, and correlation ID middleware.
    /// </summary>
    public static class TelemetryExamples
    {
        /// <summary>
        /// Wrapping a sync store with instrumentation and observing metrics via MeterListener.
        /// </summary>
        public static void RunStoreInstrumentationExample()
        {
            ExampleOutput.WriteLine("=== Store Instrumentation Example ===\n");

            // Create an in-memory store (simulating a real store)
            var innerStore = new InMemoryProductStore();

            // Wrap with instrumentation — all CRUD operations will now emit metrics and traces
            var instrumented = new InstrumentedStoreWrapper<InMemoryProductStore, TelemetryProduct>(innerStore);
            ExampleOutput.WriteLine("Wrapped InMemoryProductStore with InstrumentedStoreWrapper");
            ExampleOutput.WriteLine($"  Store type tag: {typeof(InMemoryProductStore).FullName}");
            ExampleOutput.WriteLine($"  Entity type tag: {typeof(TelemetryProduct).FullName}");

            // Set up a MeterListener to capture metrics
            var metrics = new List<string>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == BirkoTelemetryConventions.MeterName)
                    l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                metrics.Add($"  {instrument.Name} = {value:F2}ms");
            });
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                metrics.Add($"  {instrument.Name} = {value}");
            });
            listener.Start();

            ExampleOutput.WriteLine("\nPerforming instrumented operations...\n");

            // Create
            var product = new TelemetryProduct { Name = "Wireless Mouse", Price = 29.99m };
            var guid = instrumented.Create(product);
            ExampleOutput.WriteSuccess($"Create returned GUID: {guid}");

            // Read
            var found = instrumented.Read(guid);
            ExampleOutput.WriteSuccess($"Read by GUID: {found?.Name}");

            // Count
            var count = instrumented.Count();
            ExampleOutput.WriteSuccess($"Count: {count}");

            // Update
            product.Price = 24.99m;
            instrumented.Update(product);
            ExampleOutput.WriteSuccess("Update: price changed to 24.99");

            // Save (creates or updates)
            var saved = instrumented.Save(product);
            ExampleOutput.WriteSuccess($"Save returned GUID: {saved}");

            // Delete
            instrumented.Delete(product);
            ExampleOutput.WriteSuccess("Delete: product removed");

            // Show captured metrics
            ExampleOutput.WriteLine("\nCaptured metrics:");
            foreach (var m in metrics)
            {
                ExampleOutput.WriteDim(m);
            }

            ExampleOutput.WriteLine($"\nTotal metric recordings: {metrics.Count}");
            ExampleOutput.WriteLine("Each operation records: duration histogram + operation counter");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Wrapping an async store and observing async metrics.
        /// </summary>
        public static async Task RunAsyncInstrumentationExample()
        {
            ExampleOutput.WriteLine("=== Async Store Instrumentation Example ===\n");

            var innerStore = new InMemoryAsyncProductStore();
            var instrumented = new AsyncInstrumentedStoreWrapper<InMemoryAsyncProductStore, TelemetryProduct>(innerStore);

            ExampleOutput.WriteLine("Wrapped with AsyncInstrumentedStoreWrapper\n");

            // Capture metrics
            var durations = new List<double>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == BirkoTelemetryConventions.MeterName)
                    l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
            {
                if (instrument.Name == BirkoTelemetryConventions.OperationDurationMetric)
                    durations.Add(value);
            });
            listener.Start();

            // Async CRUD
            var product = new TelemetryProduct { Name = "Keyboard", Price = 49.99m };

            var guid = await instrumented.CreateAsync(product);
            ExampleOutput.WriteSuccess($"CreateAsync: {guid}");

            var found = await instrumented.ReadAsync(guid);
            ExampleOutput.WriteSuccess($"ReadAsync: {found?.Name}");

            var count = await instrumented.CountAsync();
            ExampleOutput.WriteSuccess($"CountAsync: {count}");

            await instrumented.UpdateAsync(product);
            ExampleOutput.WriteSuccess("UpdateAsync: done");

            await instrumented.DeleteAsync(product);
            ExampleOutput.WriteSuccess("DeleteAsync: done");

            ExampleOutput.WriteLine($"\nOperation durations captured: {durations.Count}");
            for (int i = 0; i < durations.Count; i++)
            {
                ExampleOutput.WriteDim($"  Operation {i + 1}: {durations[i]:F3}ms");
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Distributed tracing with ActivitySource — shows how activities are created per operation.
        /// </summary>
        public static void RunDistributedTracingExample()
        {
            ExampleOutput.WriteLine("=== Distributed Tracing Example ===\n");

            // Register an ActivityListener to capture traces
            var activities = new List<(string Name, string Status)>();
            using var activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == BirkoTelemetryConventions.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    activities.Add((activity.OperationName, activity.Status.ToString()));
                }
            };
            ActivitySource.AddActivityListener(activityListener);

            ExampleOutput.WriteLine($"ActivitySource name: {BirkoTelemetryConventions.ActivitySourceName}");
            ExampleOutput.WriteLine("Listening for activities...\n");

            var store = new InMemoryProductStore();
            var instrumented = new InstrumentedStoreWrapper<InMemoryProductStore, TelemetryProduct>(store);

            // Operations that create activities
            var product = new TelemetryProduct { Name = "Monitor", Price = 299.99m };
            instrumented.Create(product);
            instrumented.Read(product.Guid ?? Guid.Empty);
            instrumented.Count();
            instrumented.Update(product);
            instrumented.Delete(product);

            // Show error activity
            try
            {
                var errorStore = new FailingStore();
                var errorWrapper = new InstrumentedStoreWrapper<FailingStore, TelemetryProduct>(errorStore);
                errorWrapper.Read(Guid.NewGuid());
            }
            catch { /* expected */ }

            ExampleOutput.WriteLine("Captured activities:");
            foreach (var (name, status) in activities)
            {
                if (status == "Error")
                    ExampleOutput.WriteError($"{name} — Status: {status}");
                else
                    ExampleOutput.WriteSuccess($"{name} — Status: {status}");
            }

            ExampleOutput.WriteLine($"\nTotal activities: {activities.Count}");
            ExampleOutput.WriteLine("Each activity includes tags: store.type, entity_type, operation, bulk");

            ExampleOutput.WriteLine("\nActivity tags per operation:");
            ExampleOutput.WriteDim($"  {BirkoTelemetryConventions.StoreTypeTag}");
            ExampleOutput.WriteDim($"  {BirkoTelemetryConventions.EntityTypeTag}");
            ExampleOutput.WriteDim($"  {BirkoTelemetryConventions.OperationTag}");
            ExampleOutput.WriteDim($"  {BirkoTelemetryConventions.BulkTag}");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Fluent extension methods for wrapping stores with instrumentation.
        /// </summary>
        public static void RunExtensionMethodsExample()
        {
            ExampleOutput.WriteLine("=== Extension Methods Example ===\n");

            ExampleOutput.WriteLine("Fluent API to wrap any store with instrumentation:\n");

            // Sync store
            var syncStore = new InMemoryProductStore();
            var instrumented = syncStore.WithInstrumentation<InMemoryProductStore, TelemetryProduct>();
            ExampleOutput.WriteSuccess("store.WithInstrumentation<TStore, T>() — wraps IStore<T>");

            // The wrapper implements IStoreWrapper<T>
            var inner = instrumented.GetInnerStoreAs<InMemoryProductStore>();
            ExampleOutput.WriteInfo("GetInnerStoreAs<T>", inner != null ? "Returns inner store" : "null");

            // Async store
            var asyncStore = new InMemoryAsyncProductStore();
            var asyncInstrumented = asyncStore.WithAsyncInstrumentation<InMemoryAsyncProductStore, TelemetryProduct>();
            ExampleOutput.WriteSuccess("store.WithAsyncInstrumentation<TStore, T>() — wraps IAsyncStore<T>");

            ExampleOutput.WriteLine("\nAll extension methods:");
            ExampleOutput.WriteDim("  store.WithInstrumentation<TStore, T>()          → InstrumentedStoreWrapper");
            ExampleOutput.WriteDim("  store.WithBulkInstrumentation<TStore, T>()      → InstrumentedBulkStoreWrapper");
            ExampleOutput.WriteDim("  store.WithAsyncInstrumentation<TStore, T>()     → AsyncInstrumentedStoreWrapper");
            ExampleOutput.WriteDim("  store.WithAsyncBulkInstrumentation<TStore, T>() → AsyncInstrumentedBulkStoreWrapper");

            ExampleOutput.WriteLine("\nWrapper hierarchy:");
            ExampleOutput.WriteDim("  InstrumentedStoreWrapper          : IStore<T>, IStoreWrapper<T>");
            ExampleOutput.WriteDim("  InstrumentedBulkStoreWrapper      : InstrumentedStoreWrapper, IBulkStore<T>");
            ExampleOutput.WriteDim("  AsyncInstrumentedStoreWrapper     : IAsyncStore<T>, IStoreWrapper<T>");
            ExampleOutput.WriteDim("  AsyncInstrumentedBulkStoreWrapper : AsyncInstrumentedStoreWrapper, IAsyncBulkStore<T>");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Correlation ID middleware configuration and ASP.NET Core integration.
        /// </summary>
        public static void RunCorrelationIdExample()
        {
            ExampleOutput.WriteLine("=== Correlation ID Middleware Example ===\n");

            ExampleOutput.WriteLine("CorrelationIdMiddleware reads or generates a correlation ID from HTTP headers");
            ExampleOutput.WriteLine("and propagates it via Activity.Current baggage for distributed tracing.\n");

            ExampleOutput.WriteLine("ASP.NET Core registration:");
            ExampleOutput.WriteDim("  // In Program.cs / Startup.cs");
            ExampleOutput.WriteDim("  builder.Services.AddBirkoTelemetry(options =>");
            ExampleOutput.WriteDim("  {");
            ExampleOutput.WriteDim("      options.EnableCorrelationId = true;");
            ExampleOutput.WriteDim("      options.CorrelationIdHeaderName = \"X-Correlation-Id\";");
            ExampleOutput.WriteDim("  });");
            ExampleOutput.WriteDim("  ");
            ExampleOutput.WriteDim("  app.UseBirkoCorrelationId();");

            ExampleOutput.WriteLine("\nBehavior:");
            ExampleOutput.WriteSuccess("Reads X-Correlation-Id from request header (if present)");
            ExampleOutput.WriteSuccess("Generates a new GUID if header is missing");
            ExampleOutput.WriteSuccess("Sets Activity.Current.SetBaggage(\"correlation-id\", value)");
            ExampleOutput.WriteSuccess("Echoes the correlation ID in the response header");

            // Show options
            var options = new BirkoTelemetryOptions();
            ExampleOutput.WriteLine("\nBirkoTelemetryOptions defaults:");
            ExampleOutput.WriteInfo("EnableCorrelationId", options.EnableCorrelationId.ToString());
            ExampleOutput.WriteInfo("CorrelationIdHeaderName", options.CorrelationIdHeaderName);

            ExampleOutput.WriteLine("\nConventions (BirkoTelemetryConventions):");
            ExampleOutput.WriteInfo("MeterName", BirkoTelemetryConventions.MeterName);
            ExampleOutput.WriteInfo("ActivitySourceName", BirkoTelemetryConventions.ActivitySourceName);
            ExampleOutput.WriteInfo("DurationMetric", BirkoTelemetryConventions.OperationDurationMetric);
            ExampleOutput.WriteInfo("CountMetric", BirkoTelemetryConventions.OperationCountMetric);
            ExampleOutput.WriteInfo("ErrorMetric", BirkoTelemetryConventions.OperationErrorMetric);
            ExampleOutput.WriteInfo("CorrelationHeader", BirkoTelemetryConventions.DefaultCorrelationIdHeader);

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Shows how error metrics are recorded when store operations fail.
        /// </summary>
        public static void RunErrorTrackingExample()
        {
            ExampleOutput.WriteLine("=== Error Tracking Example ===\n");

            var errorCounts = 0;
            var successCounts = 0;
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == BirkoTelemetryConventions.MeterName)
                    l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                if (instrument.Name == BirkoTelemetryConventions.OperationErrorMetric)
                    errorCounts++;
                else if (instrument.Name == BirkoTelemetryConventions.OperationCountMetric)
                    successCounts++;
            });
            listener.Start();

            // Successful operation
            var store = new InMemoryProductStore();
            var instrumented = new InstrumentedStoreWrapper<InMemoryProductStore, TelemetryProduct>(store);
            instrumented.Count();
            ExampleOutput.WriteSuccess("Count operation: success");

            // Failing operation
            var failStore = new FailingStore();
            var failWrapper = new InstrumentedStoreWrapper<FailingStore, TelemetryProduct>(failStore);
            try
            {
                failWrapper.Read(Guid.NewGuid());
            }
            catch (InvalidOperationException ex)
            {
                ExampleOutput.WriteError($"Read operation: {ex.Message}");
            }

            try
            {
                failWrapper.Create(new TelemetryProduct());
            }
            catch (InvalidOperationException ex)
            {
                ExampleOutput.WriteError($"Create operation: {ex.Message}");
            }

            ExampleOutput.WriteLine($"\nMetric counters:");
            ExampleOutput.WriteInfo("Operation count", $"{successCounts} (includes both success and error)");
            ExampleOutput.WriteInfo("Error count", $"{errorCounts}");
            ExampleOutput.WriteLine("\nOn error, the instrumentation records:");
            ExampleOutput.WriteDim($"  {BirkoTelemetryConventions.OperationDurationMetric} — duration before failure");
            ExampleOutput.WriteDim($"  {BirkoTelemetryConventions.OperationCountMetric} — incremented");
            ExampleOutput.WriteDim($"  {BirkoTelemetryConventions.OperationErrorMetric} — incremented");
            ExampleOutput.WriteDim("  Activity.Status = Error (with exception message)");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        #region Example Stores

        public class TelemetryProduct : AbstractLogModel
        {
            public string? Name { get; set; }
            public decimal Price { get; set; }
        }

        /// <summary>Simple in-memory IStore for demonstration.</summary>
        public class InMemoryProductStore : IStore<TelemetryProduct>
        {
            private readonly Dictionary<Guid, TelemetryProduct> _data = new();

            public void Init() { }
            public void Destroy() => _data.Clear();
            public TelemetryProduct CreateInstance() => new();

            public long Count(Expression<Func<TelemetryProduct, bool>>? filter = null) => _data.Count;

            public TelemetryProduct? Read(Guid guid) => _data.TryGetValue(guid, out var v) ? v : null;

            public TelemetryProduct? Read(Expression<Func<TelemetryProduct, bool>>? filter = null)
            {
                foreach (var v in _data.Values) return v;
                return null;
            }

            public Guid Create(TelemetryProduct data, StoreDataDelegate<TelemetryProduct>? storeDelegate = null)
            {
                data.Guid = Guid.NewGuid();
                if (storeDelegate != null) data = storeDelegate(data);
                _data[data.Guid.Value] = data;
                return data.Guid.Value;
            }

            public void Update(TelemetryProduct data, StoreDataDelegate<TelemetryProduct>? storeDelegate = null)
            {
                if (data.Guid.HasValue)
                {
                    if (storeDelegate != null) data = storeDelegate(data);
                    _data[data.Guid.Value] = data;
                }
            }

            public void Delete(TelemetryProduct data)
            {
                if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
            }

            public Guid Save(TelemetryProduct data, StoreDataDelegate<TelemetryProduct>? storeDelegate = null)
            {
                if (data.Guid == null || data.Guid == Guid.Empty)
                    return Create(data, storeDelegate);
                Update(data, storeDelegate);
                return data.Guid.Value;
            }
        }

        /// <summary>Simple in-memory IAsyncStore for demonstration.</summary>
        public class InMemoryAsyncProductStore : IAsyncStore<TelemetryProduct>
        {
            private readonly Dictionary<Guid, TelemetryProduct> _data = new();

            public Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task DestroyAsync(CancellationToken ct = default) { _data.Clear(); return Task.CompletedTask; }
            public TelemetryProduct CreateInstance() => new();

            public Task<long> CountAsync(Expression<Func<TelemetryProduct, bool>>? filter = null, CancellationToken ct = default)
                => Task.FromResult((long)_data.Count);

            public Task<TelemetryProduct?> ReadAsync(Guid guid, CancellationToken ct = default)
                => Task.FromResult(_data.TryGetValue(guid, out var v) ? v : null);

            public Task<TelemetryProduct?> ReadAsync(Expression<Func<TelemetryProduct, bool>>? filter = null, CancellationToken ct = default)
            {
                foreach (var v in _data.Values) return Task.FromResult<TelemetryProduct?>(v);
                return Task.FromResult<TelemetryProduct?>(null);
            }

            public Task<Guid> CreateAsync(TelemetryProduct data, StoreDataDelegate<TelemetryProduct>? processDelegate = null, CancellationToken ct = default)
            {
                data.Guid = Guid.NewGuid();
                if (processDelegate != null) data = processDelegate(data);
                _data[data.Guid.Value] = data;
                return Task.FromResult(data.Guid.Value);
            }

            public Task UpdateAsync(TelemetryProduct data, StoreDataDelegate<TelemetryProduct>? processDelegate = null, CancellationToken ct = default)
            {
                if (data.Guid.HasValue)
                {
                    if (processDelegate != null) data = processDelegate(data);
                    _data[data.Guid.Value] = data;
                }
                return Task.CompletedTask;
            }

            public Task DeleteAsync(TelemetryProduct data, CancellationToken ct = default)
            {
                if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
                return Task.CompletedTask;
            }

            public Task<Guid> SaveAsync(TelemetryProduct data, StoreDataDelegate<TelemetryProduct>? processDelegate = null, CancellationToken ct = default)
            {
                if (data.Guid == null || data.Guid == Guid.Empty)
                    return CreateAsync(data, processDelegate, ct);
                return Task.FromResult(data.Guid.Value);
            }
        }

        /// <summary>Store that always throws — used to demonstrate error metrics.</summary>
        public class FailingStore : IStore<TelemetryProduct>
        {
            public void Init() { }
            public void Destroy() { }
            public TelemetryProduct CreateInstance() => new();
            public long Count(Expression<Func<TelemetryProduct, bool>>? filter = null) => throw new InvalidOperationException("Store failure");
            public TelemetryProduct? Read(Guid guid) => throw new InvalidOperationException("Store failure");
            public TelemetryProduct? Read(Expression<Func<TelemetryProduct, bool>>? filter = null) => throw new InvalidOperationException("Store failure");
            public Guid Create(TelemetryProduct data, StoreDataDelegate<TelemetryProduct>? storeDelegate = null) => throw new InvalidOperationException("Store failure");
            public void Update(TelemetryProduct data, StoreDataDelegate<TelemetryProduct>? storeDelegate = null) => throw new InvalidOperationException("Store failure");
            public void Delete(TelemetryProduct data) => throw new InvalidOperationException("Store failure");
            public Guid Save(TelemetryProduct data, StoreDataDelegate<TelemetryProduct>? storeDelegate = null) => throw new InvalidOperationException("Store failure");
        }

        #endregion
    }
}
