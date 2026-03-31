namespace Birko.AI.Tools
{
    public abstract class Tool
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract object? InputSchema { get; }

        public Action<string, string>? MessageCallback { get; set; }
        public AgentOptions? Options { get; set; }

        /// <summary>
        /// Async tool execution. Override this for tools that perform I/O, database, or network operations.
        /// Default implementation calls the synchronous Execute method for backwards compatibility.
        /// </summary>
        public virtual Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input)
        {
            return Task.FromResult(Execute(workingDirectory, input));
        }

        /// <summary>
        /// Synchronous tool execution. Override this for simple tools that don't perform async I/O.
        /// Tools that need async should override ExecuteAsync instead.
        /// </summary>
        public virtual string Execute(string workingDirectory, Dictionary<string, object> input)
        {
            throw new NotImplementedException($"Tool '{Name}' must override either Execute or ExecuteAsync.");
        }

        protected void SendMessage(string type, string content)
        {
            MessageCallback?.Invoke(type, content);
        }
    }
}
