using System.Collections.Generic;
using Birko.AI.Models;

namespace Birko.AI.Agents
{
    /// <summary>How a run ended. Set in one place, where the run loop decides to stop (TASK-660).</summary>
    public enum AgentRunOutcome
    {
        /// <summary>The model ended its turn (<c>end_turn</c>): the task completed as far as the model is concerned.</summary>
        Completed,

        /// <summary>The LLM request failed (<c>error</c>). <see cref="AgentRunResult.Failure"/> carries the classified cause.</summary>
        LlmFailed,

        /// <summary>The provider is not configured (<c>NotConfigured</c>).</summary>
        NotConfigured,

        /// <summary>The iteration budget ran out while the model was still calling tools: the task may be incomplete.</summary>
        MaxIterationsReached,

        /// <summary>The model's output was cut off by its token limit (<c>max_tokens</c>).</summary>
        Truncated,

        /// <summary>The model stopped for a tool call but sent none.</summary>
        MissingToolCall,

        /// <summary>An <see cref="IAgentHooks.OnIterationStartingAsync"/> hook asked the run to stop.</summary>
        StoppedByHook,

        /// <summary>
        /// Any other stop reason (e.g. <c>content_filter</c>, or one a provider invented). <see cref="AgentRunResult.StopReason"/>
        /// carries it.
        /// </summary>
        UnexpectedStop,
    }

    /// <summary>
    /// The result of a run: the conversation, as <see cref="Agent.RunAsync"/> returns it, plus how the run ended.
    /// </summary>
    /// <param name="Conversation">The conversation, the same list the conversation-returning methods return.</param>
    /// <param name="Outcome">How the run ended.</param>
    /// <param name="Failure">
    /// The classified cause of an <see cref="AgentRunOutcome.LlmFailed"/> or <see cref="AgentRunOutcome.NotConfigured"/> run
    /// (TASK-531), when the provider reported one; otherwise null.
    /// </param>
    /// <param name="StopReason">
    /// The model's stop reason for the turn that ended the run (<c>end_turn</c>, <c>error</c>, <c>max_tokens</c>, …); null
    /// when no model turn ended it (a hook stop before the first call).
    /// </param>
    /// <param name="Detail">A human-readable note: the LLM error message, or a hook's stop reason; otherwise null.</param>
    public sealed record AgentRunResult(
        List<Message> Conversation,
        AgentRunOutcome Outcome,
        LlmFailure? Failure = null,
        string? StopReason = null,
        string? Detail = null)
    {
        /// <summary>True when the run ended with <see cref="AgentRunOutcome.Completed"/>.</summary>
        public bool IsCompleted => Outcome == AgentRunOutcome.Completed;
    }
}
