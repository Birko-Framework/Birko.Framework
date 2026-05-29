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
        /// Tool execution entry point. Performs I/O, database, or network operations as needed.
        /// </summary>
        public abstract Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input);

        protected void SendMessage(string type, string content)
        {
            MessageCallback?.Invoke(type, content);
        }
    }
}
