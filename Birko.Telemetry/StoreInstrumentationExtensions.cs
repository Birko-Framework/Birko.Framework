using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Telemetry;

/// <summary>
/// Fluent extension methods to wrap stores with instrumentation.
/// </summary>
public static class StoreInstrumentationExtensions
{
    /// <summary>
    /// Wraps an <see cref="IStore{T}"/> with instrumentation that records metrics and traces.
    /// </summary>
    public static InstrumentedStoreWrapper<TStore, T> WithInstrumentation<TStore, T>(this TStore store)
        where TStore : IStore<T>
        where T : Data.Models.AbstractModel
    {
        return new InstrumentedStoreWrapper<TStore, T>(store);
    }

    /// <summary>
    /// Wraps an <see cref="IBulkStore{T}"/> with instrumentation that records metrics and traces.
    /// </summary>
    public static InstrumentedBulkStoreWrapper<TStore, T> WithBulkInstrumentation<TStore, T>(this TStore store)
        where TStore : IBulkStore<T>
        where T : Data.Models.AbstractModel
    {
        return new InstrumentedBulkStoreWrapper<TStore, T>(store);
    }

    /// <summary>
    /// Wraps an <see cref="IAsyncStore{T}"/> with instrumentation that records metrics and traces.
    /// </summary>
    public static AsyncInstrumentedStoreWrapper<TStore, T> WithAsyncInstrumentation<TStore, T>(this TStore store)
        where TStore : IAsyncStore<T>
        where T : Data.Models.AbstractModel
    {
        return new AsyncInstrumentedStoreWrapper<TStore, T>(store);
    }

    /// <summary>
    /// Wraps an <see cref="IAsyncBulkStore{T}"/> with instrumentation that records metrics and traces.
    /// </summary>
    public static AsyncInstrumentedBulkStoreWrapper<TStore, T> WithAsyncBulkInstrumentation<TStore, T>(this TStore store)
        where TStore : IAsyncBulkStore<T>
        where T : Data.Models.AbstractModel
    {
        return new AsyncInstrumentedBulkStoreWrapper<TStore, T>(store);
    }
}
