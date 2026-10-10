using Birko.AI.Models;

namespace Birko.AI.Agents
{
    /// <summary>
    /// Observes and steers an <see cref="Agent"/> run from outside the agent. Attach through
    /// <see cref="Agent.Hooks"/>. Every hook fires on the streaming and the non-streaming path alike.
    /// <para>
    /// A hook that throws ends the run with an <see cref="AgentHookException"/> wrapping the hook's
    /// exception; an <see cref="OperationCanceledException"/> propagates unwrapped. A hook failure is
    /// never retried through <see cref="AgentOptions.StreamingFallbackToSync"/>. A tool hook that throws
    /// part-way through a batch leaves the assistant's tool_use turn without its tool_result turn — the
    /// same state a throwing tool leaves — so that conversation is not resumable as is.
    /// </para>
    /// <para>
    /// Each hook fires once per iteration and tool call. When a streaming request falls back to sync,
    /// only that model call is retried; the iteration's <see cref="AgentIterationContext.IsStreaming"/>
    /// still reads <c>true</c>, because it was decided before the call.
    /// </para>
    /// </summary>
    public interface IAgentHooks
    {
        /// <summary>
        /// Fires before each model call, before the run's cancellation is checked — so a hook can
        /// <see cref="AgentIterationContext.Stop"/> a cancelled run cleanly instead of letting it throw.
        /// </summary>
        ValueTask OnIterationStartingAsync(AgentIterationContext context);

        /// <summary>Fires before a tool requested by the model is executed (or found missing).</summary>
        ValueTask OnToolCallStartingAsync(ToolCallStarted call, CancellationToken cancellationToken);

        /// <summary>
        /// Fires after a tool call, including one that threw — <see cref="ToolCallFinished.Exception"/> is
        /// then set, and the tool's exception still propagates out of the run once this hook returns.
        /// </summary>
        ValueTask OnToolCallFinishedAsync(ToolCallFinished call, CancellationToken cancellationToken);
    }

    /// <summary>
    /// <see cref="IAgentHooks"/> with no-op defaults — override only the hooks you need.
    /// </summary>
    public abstract class AgentHooks : IAgentHooks
    {
        public virtual ValueTask OnIterationStartingAsync(AgentIterationContext context) => ValueTask.CompletedTask;

        public virtual ValueTask OnToolCallStartingAsync(ToolCallStarted call, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public virtual ValueTask OnToolCallFinishedAsync(ToolCallFinished call, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    /// <summary>
    /// What <see cref="IAgentHooks.OnIterationStartingAsync"/> sees of the iteration about to run, and
    /// the two things it can do to it: add a user message, or stop the run.
    /// </summary>
    public sealed class AgentIterationContext
    {
        private readonly List<string> _pendingMessages = new();

        internal AgentIterationContext(int iteration, int maxIterations, bool isStreaming, string systemPrompt,
            IReadOnlyList<Message> conversation, CancellationToken cancellationToken)
        {
            Iteration = iteration;
            MaxIterations = maxIterations;
            IsStreaming = isStreaming;
            SystemPrompt = systemPrompt;
            Conversation = conversation;
            CancellationToken = cancellationToken;
        }

        /// <summary>1-based iteration number within this run.</summary>
        public int Iteration { get; }

        public int MaxIterations { get; }

        /// <summary>Whether this iteration's model call is the streaming one.</summary>
        public bool IsStreaming { get; }

        /// <summary>The exact system prompt sent with this iteration's model call.</summary>
        public string SystemPrompt { get; }

        /// <summary>
        /// A read-only <b>live</b> view of the run's conversation — it keeps growing after the hook
        /// returns. Copy it to keep a snapshot.
        /// </summary>
        public IReadOnlyList<Message> Conversation { get; }

        public CancellationToken CancellationToken { get; }

        public bool StopRequested { get; private set; }

        public string? StopReason { get; private set; }

        /// <summary>
        /// Appends a user message to the conversation; it is sent with THIS iteration's model call. A
        /// message added after a tool-result turn makes two consecutive user turns.
        /// </summary>
        public void AppendUserMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("An appended message must not be blank.", nameof(content));

            _pendingMessages.Add(content);
        }

        /// <summary>
        /// Ends the run before this iteration's model call. Messages appended in the same hook call are
        /// still added to the returned conversation.
        /// </summary>
        public void Stop(string? reason = null)
        {
            StopRequested = true;
            StopReason = reason;
        }

        internal IReadOnlyList<string> PendingMessages => _pendingMessages;
    }

    /// <summary>A tool call about to execute.</summary>
    public sealed record ToolCallStarted(
        int Iteration,
        string ToolUseId,
        string ToolName,
        IReadOnlyDictionary<string, object> Input);

    /// <summary>
    /// A tool call that has finished. <see cref="IsError"/> is set for an unknown tool, a result
    /// beginning <c>Error:</c>, or a thrown <see cref="Exception"/>; <see cref="Result"/> is null only
    /// when the tool threw.
    /// </summary>
    public sealed record ToolCallFinished(
        int Iteration,
        string ToolUseId,
        string ToolName,
        IReadOnlyDictionary<string, object> Input,
        string? Result,
        bool ToolFound,
        bool IsError,
        Exception? Exception,
        TimeSpan Duration);

    /// <summary>
    /// An <see cref="IAgentHooks"/> member threw. <see cref="Exception.InnerException"/> is the hook's
    /// exception; <see cref="ToolException"/> is the tool's, when the hook threw while reporting one.
    /// </summary>
    public sealed class AgentHookException : Exception
    {
        public AgentHookException(string hookName, int iteration, Exception innerException, Exception? toolException = null)
            : base($"Agent hook '{hookName}' failed at iteration {iteration}: {innerException.Message}", innerException)
        {
            HookName = hookName;
            Iteration = iteration;
            ToolException = toolException;
        }

        public string HookName { get; }

        public int Iteration { get; }

        public Exception? ToolException { get; }
    }
}
