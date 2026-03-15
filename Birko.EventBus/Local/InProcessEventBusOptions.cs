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
