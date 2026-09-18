using System;

namespace Birko.EventBus.Local
{
    /// <summary>
    /// Options for <see cref="InProcessEventBus"/>.
    /// </summary>
    public class InProcessEventBusOptions
    {
        /// <summary>
        /// Maximum number of concurrent handler executions per publish.
        /// 1 = sequential (default), &gt;1 = parallel dispatch with SemaphoreSlim.
        /// </summary>
        public int MaxConcurrency { get; set; } = 1;

        /// <summary>
        /// What happens when a handler throws an exception.
        /// </summary>
        public ErrorHandlingMode ErrorHandling { get; set; } = ErrorHandlingMode.Continue;

        /// <summary>
        /// CR-M185: optional callback invoked with the event and the exception whenever a handler
        /// throws — for both <see cref="ErrorHandlingMode.Continue"/> (before continuing) and
        /// <see cref="ErrorHandlingMode.Stop"/> (before the exception propagates). Wire this to your
        /// logger. Without it the bus has no logging dependency, so a throwing handler is dropped
        /// silently in Continue mode; set this to observe those failures.
        /// </summary>
        public Action<IEvent, Exception>? OnHandlerError { get; set; }
    }

    /// <summary>
    /// Error handling strategy for event dispatch.
    /// </summary>
    public enum ErrorHandlingMode
    {
        /// <summary>
        /// Log and continue to the next handler. Default.
        /// </summary>
        Continue,

        /// <summary>
        /// Stop dispatching to remaining handlers on first failure.
        /// </summary>
        Stop
    }
}
