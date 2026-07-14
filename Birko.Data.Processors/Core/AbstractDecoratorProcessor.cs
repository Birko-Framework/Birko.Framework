using Microsoft.Extensions.Logging;

namespace Birko.Data.Processors;

/// <summary>
/// Base for processor decorators that wrap an inner <see cref="IStreamProcessor"/> (e.g. download-then-process,
/// unzip-then-process). Holds the inner processor and forwards the whole event pipeline
/// (OnItemProcessed(Sync) / OnProcessFinished(Sync) / OnElementStart/Value/End) from this outer decorator to
/// the inner processor once, so each concrete decorator only implements its transport/extract step.
/// </summary>
/// <typeparam name="TProcessor">The inner stream-processor type.</typeparam>
/// <typeparam name="TModel">The model type being built during processing.</typeparam>
public abstract class AbstractDecoratorProcessor<TProcessor, TModel> : AbstractProcessor<TModel>
    where TProcessor : AbstractProcessor<TModel>, IStreamProcessor
    where TModel : new()
{
    /// <summary>The wrapped inner processor.</summary>
    protected readonly TProcessor _inner;

    protected AbstractDecoratorProcessor(TProcessor inner, ILogger? logger = null)
        : base(logger)
    {
        _inner = inner;
        WireInnerEvents();
    }

    /// <summary>The inner processor for direct configuration access.</summary>
    public TProcessor Inner => _inner;

    /// <summary>
    /// Forwards this decorator's event pipeline to the inner processor. The lambdas read the outer
    /// properties at invocation time, so wiring during base construction (before derived fields are set)
    /// is safe.
    /// </summary>
    private void WireInnerEvents()
    {
        _inner.OnItemProcessed = async (item, ct) =>
        {
            if (OnItemProcessed != null)
            {
                await OnItemProcessed(item, ct).ConfigureAwait(false);
            }
        };
        _inner.OnItemProcessedSync = item => OnItemProcessedSync?.Invoke(item);
        _inner.OnProcessFinished = async ct =>
        {
            if (OnProcessFinished != null)
            {
                await OnProcessFinished(ct).ConfigureAwait(false);
            }
        };
        _inner.OnProcessFinishedSync = () => OnProcessFinishedSync?.Invoke();
        _inner.OnElementStart = name => OnElementStart?.Invoke(name);
        _inner.OnElementValue = (name, value) => OnElementValue?.Invoke(name, value);
        _inner.OnElementEnd = name => OnElementEnd?.Invoke(name);
    }
}
