using Microsoft.Extensions.Logging;

namespace Birko.Data.Processors;

/// <summary>
/// Generic base processor with item lifecycle and event pipeline.
/// Subclasses populate <see cref="_item"/> during parsing, then call
/// <see cref="PostProcessItemAsync"/> or <see cref="PostProcessItem"/> to emit the completed item.
/// </summary>
/// <typeparam name="T">The model type being built during processing.</typeparam>
public abstract class AbstractProcessor<T> : IProcessor where T : new()
{
    private readonly ILogger? _logger;

    /// <summary>Current item being populated during parsing.</summary>
    protected T _item = new();

    /// <summary>Fires after each item is fully parsed (async).</summary>
    public Func<T, CancellationToken, Task>? OnItemProcessed { get; set; }

    /// <summary>Fires after each item is fully parsed (sync).</summary>
    public Action<T>? OnItemProcessedSync { get; set; }

    /// <summary>Fires after all processing is complete (async).</summary>
    public Func<CancellationToken, Task>? OnProcessFinished { get; set; }

    /// <summary>Fires after all processing is complete (sync).</summary>
    public Action? OnProcessFinishedSync { get; set; }

    /// <summary>Fires when a named element starts (XML element open, CSV column index).</summary>
    public Action<string>? OnElementStart { get; set; }

    /// <summary>Fires with element name and its text value.</summary>
    public Action<string, string>? OnElementValue { get; set; }

    /// <summary>Fires when a named element ends.</summary>
    public Action<string>? OnElementEnd { get; set; }

    protected AbstractProcessor(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Reset current item to a fresh instance.</summary>
    protected void InitItem() => _item = new T();

    /// <summary>Fire OnItemProcessed (async) and reset the item.</summary>
    protected async Task PostProcessItemAsync(CancellationToken cancellationToken)
    {
        if (OnItemProcessed != null)
        {
            await OnItemProcessed(_item, cancellationToken).ConfigureAwait(false);
        }

        InitItem();
    }

    /// <summary>Fire OnItemProcessedSync (sync) and reset the item.</summary>
    protected void PostProcessItem()
    {
        OnItemProcessedSync?.Invoke(_item);
        InitItem();
    }

    /// <summary>Fire OnProcessFinished (async).</summary>
    protected async Task InvokeProcessFinishedAsync(CancellationToken cancellationToken)
    {
        if (OnProcessFinished != null)
        {
            await OnProcessFinished(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Fire OnProcessFinishedSync (sync).</summary>
    protected void InvokeProcessFinished()
    {
        OnProcessFinishedSync?.Invoke();
    }

    protected void Log(LogLevel level, string message, params object[] args)
    {
        _logger?.Log(level, message, args);
    }

    public abstract void Process();
    public abstract Task ProcessAsync(CancellationToken cancellationToken = default);
}
