using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;

namespace Birko.AI.Agents
{
    public abstract class Agent
    {
        private readonly ILlmProvider _llmProvider;
        private readonly AgentOptions _options;
        private List<Tool> _tools;
        private Action<string, string>? _messageCallback;

        protected Agent(ILlmProvider llmProvider, AgentOptions? options = null, Action<string, string>? messageCallback = null)
        {
            _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
            _options = options ?? new AgentOptions();
            _tools = CreateTools();
            _messageCallback = messageCallback;

            _llmProvider.MessageCallback = messageCallback;

            foreach (var tool in _tools)
            {
                tool.MessageCallback = messageCallback;
                tool.Options = _options;
            }
        }

        public void SetMessageCallback(Action<string, string>? callback)
        {
            _messageCallback = callback;
            _llmProvider.MessageCallback = callback;

            foreach (var tool in _tools)
            {
                tool.MessageCallback = callback;
                tool.Options = _options;
            }
        }

        protected void SendMessage(string type, string content)
        {
            _messageCallback?.Invoke(type, content);
        }

        protected abstract string SystemPrompt { get; }

        protected virtual List<Tool> CreateTools()
        {
            return
            [
                new ListFilesTool(),
                new ReadFileTool(),
                new WriteFileTool(),
                new EditFileTool(),
                new AppendToFileTool(),
                new SearchCodeTool(),
                new RunCommandTool(),
                new DisplayTextTool(),
                new AskUserTool()
            ];
        }

        /// <summary>
        /// Rebuilds the tools list. Call this from derived class constructors
        /// after setting fields that CreateTools() depends on.
        /// </summary>
        protected void RebuildTools()
        {
            _tools = CreateTools();
            foreach (var tool in _tools)
            {
                tool.MessageCallback = _messageCallback;
                tool.Options = _options;
            }
        }

        public ILlmProvider Provider => _llmProvider;
        public IReadOnlyList<Tool> Tools => _tools;
        public string ProviderName => _llmProvider.Name;
        public AgentOptions Options => _options;

        public void AddTool(Tool tool)
        {
            tool.MessageCallback = _messageCallback;
            tool.Options = _options;
            _tools.Add(tool);
        }

        public bool RemoveTool(string toolName)
        {
            var tool = _tools.FirstOrDefault(t => t.Name == toolName);
            if (tool != null)
            {
                _tools.Remove(tool);
                return true;
            }
            return false;
        }

        public string WorkingDirectory => _options.WorkingDirectory;
        public bool Verbose => _options.Verbose;

        protected static string GetFileOperationGuidelines()
        {
            return @"- Always explore the workspace first with list_files before making assumptions
- CRITICAL: Before creating a file with write_file, check if it already exists using list_files or read_file
- If a file exists, use edit_file or append_to_file instead of write_file to preserve existing content
- Read existing files before modifying them with read_file
- Use search_code to find specific content before editing/appending
- Use edit_file for surgical changes to existing files
- Use write_file only for creating NEW files or when you explicitly want to replace entire file content
- Use append_to_file to add content to the end of a file
- When making multiple changes to the same file, use edit_file for each change or read the file first, make all changes, then write once";
        }

        protected static string GetCommonBestPractices()
        {
            return @"- Test your code after making changes
- If something fails, analyze the error and try a different approach
- Be methodical and thorough";
        }

        protected virtual string GetDepthGuidance()
        {
            return Options.ModelDepth switch
            {
                <= 3 => @"
Reasoning approach: Quick and efficient
- Make direct, straightforward decisions
- Prioritize speed over exhaustive analysis
- Use common patterns and best practices",
                >= 7 => @"
Reasoning approach: Deep and thorough
- Think carefully through multiple approaches before acting
- Consider edge cases and potential issues
- Analyze trade-offs and document your reasoning
- Be extra careful with changes that could have side effects",
                _ => @"
Reasoning approach: Balanced
- Think step-by-step about what you need to do
- Consider important edge cases
- Balance thoroughness with efficiency"
            };
        }

        /// <summary>
        /// Observes and steers runs: per-iteration and per-tool-call hooks. Read once at the start of
        /// each run. Like the tools list, it is shared by every run on this instance — an agent is not
        /// safe for concurrent runs.
        /// </summary>
        public IAgentHooks? Hooks { get; set; }

        /// <summary>
        /// The system prompt the next model call will send — <see cref="SystemPrompt"/>, readable from
        /// outside the agent.
        /// </summary>
        public string CurrentSystemPrompt => SystemPrompt;

        public async Task<List<Message>> RunAsync(string task, int? maxIterations = null, CancellationToken cancellationToken = default)
        {
            var conversation = new List<Message>
            {
                new() { Role = "user", Content = task }
            };
            return await RunWithHistoryAsync(conversation, maxIterations, cancellationToken);
        }

        public async Task<List<Message>> ContinueAsync(List<Message> conversationHistory, string newUserMessage, int? maxIterations = null, CancellationToken cancellationToken = default)
        {
            var conversation = new List<Message>(conversationHistory);
            conversation.Add(new Message { Role = "user", Content = newUserMessage });
            return await RunWithHistoryAsync(conversation, maxIterations, cancellationToken);
        }

        /// <summary>
        /// Continues a run from a saved conversation exactly as it stands — no user message is added,
        /// so a conversation ending in a tool-result turn goes to the model as the next request. The
        /// caller's sequence is copied, never modified.
        /// </summary>
        /// <exception cref="ArgumentException">The conversation is empty, or ends in an assistant turn
        /// (there is nothing for the model to answer).</exception>
        public async Task<List<Message>> ResumeAsync(IEnumerable<Message> conversation, int? maxIterations = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(conversation);

            var copy = new List<Message>(conversation);
            if (copy.Count == 0)
                throw new ArgumentException("Cannot resume from an empty conversation.", nameof(conversation));
            if (copy[^1].Role == "assistant")
                throw new ArgumentException("Cannot resume from a conversation ending in an assistant turn; use ContinueAsync to add a user message.", nameof(conversation));

            return await RunWithHistoryAsync(copy, maxIterations, cancellationToken);
        }

        private async Task<List<Message>> RunWithHistoryAsync(List<Message> conversation, int? maxIterations = null, CancellationToken cancellationToken = default)
        {
            var hooks = Hooks;

            if (_options.EnableStreaming)
            {
                // Snapshot the conversation before the streaming attempt. The streaming loop appends
                // assistant/tool messages as it runs, so a failure part-way through can leave the list
                // with a dangling assistant turn (tool_use blocks but no matching tool_result) that most
                // providers reject. Restore the pre-streaming state before the sync fallback (CR-L001).
                var snapshot = new List<Message>(conversation);
                try
                {
                    return await RunLoopAsync(conversation, streaming: true, hooks, maxIterations, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not AgentHookException && _options.StreamingFallbackToSync)
                {
                    SendMessage("warning", $"Streaming failed: {ex.Message}. Falling back to synchronous mode.");
                    conversation = snapshot;
                }
            }

            return await RunLoopAsync(conversation, streaming: false, hooks, maxIterations, cancellationToken);
        }

        // The only run loop — streaming and sync share it so hooks and the token cannot drift between paths.
        private async Task<List<Message>> RunLoopAsync(List<Message> conversation, bool streaming, IAgentHooks? hooks, int? maxIterations, CancellationToken cancellationToken)
        {
            var maxIter = maxIterations ?? _options.MaxIterations;

            for (var iteration = 1; iteration <= maxIter; iteration++)
            {
                var systemPrompt = SystemPrompt;
                var streamThisCall = streaming && iteration == 1;

                if (hooks != null && await StoppedByIterationHookAsync(hooks, conversation, iteration, maxIter, streamThisCall, systemPrompt, cancellationToken))
                    return conversation;

                cancellationToken.ThrowIfCancellationRequested();

                if (_options.Verbose)
                    SendMessage("info", $"ITERATION {iteration}{(streamThisCall ? " (streaming)" : "")}");

                LlmResponse response;
                if (streamThisCall)
                {
                    response = await GetStreamingResponseAsync(conversation, systemPrompt, cancellationToken);
                }
                else
                {
                    response = await _llmProvider.SendMessageAsync(conversation, _tools, systemPrompt, cancellationToken);
                    _options.OnLlmResponseReceived?.Invoke();
                }

                if (_options.Verbose)
                    SendMessage("info", $"Stop reason: {response.StopReason}");

                conversation.Add(new Message { Role = "assistant", Content = response.Content });

                var result = await HandleResponse(response, conversation, iteration, maxIter, hooks, cancellationToken);
                if (result.HasValue)
                    return conversation;
            }

            if (_options.Verbose)
                SendMessage("warning", $"Maximum iterations ({maxIter}) reached.");

            return conversation;
        }

        private async Task<bool> StoppedByIterationHookAsync(IAgentHooks hooks, List<Message> conversation, int iteration, int maxIter,
            bool streamThisCall, string systemPrompt, CancellationToken cancellationToken)
        {
            var context = new AgentIterationContext(iteration, maxIter, streamThisCall, systemPrompt, conversation.AsReadOnly(), cancellationToken);
            await InvokeHookAsync(nameof(IAgentHooks.OnIterationStartingAsync), iteration, () => hooks.OnIterationStartingAsync(context));

            foreach (var message in context.PendingMessages)
                conversation.Add(new Message { Role = "user", Content = message });

            if (!context.StopRequested)
                return false;

            if (_options.Verbose)
                SendMessage("info", $"Run stopped by hook at iteration {iteration}{(context.StopReason is null ? "" : $": {context.StopReason}")}");
            return true;
        }

        private async Task<LlmResponse> GetStreamingResponseAsync(List<Message> conversation, string systemPrompt, CancellationToken cancellationToken)
        {
            // await using disposes the streaming response — and its underlying HTTP
            // connection (LlmStreamingResponse.Resource) — even if enumeration is abandoned
            // early by an exception or cancellation (CR-M003).
            await using var streamingResponse = await _llmProvider.SendMessageStreamingAsync(conversation, _tools, systemPrompt, cancellationToken);
            _options.OnLlmResponseReceived?.Invoke();

            if (!string.IsNullOrEmpty(streamingResponse.Error))
            {
                SendMessage("error", $"Streaming error: {streamingResponse.Error}");
                throw new InvalidOperationException($"Streaming failed: {streamingResponse.Error}");
            }

            var fullText = new System.Text.StringBuilder();
            var stream = await streamingResponse.GetStreamAsync();

            await foreach (var chunk in stream.WithCancellation(cancellationToken))
            {
                fullText.Append(chunk);
                SendMessage("assistant_stream", chunk);
            }

            return streamingResponse.FinalResponse ?? new LlmResponse
            {
                StopReason = streamingResponse.StopReason ?? "end_turn",
                Content = new List<ContentBlock>
                {
                    new ContentBlock { Type = "text", Text = fullText.ToString() }
                }
            };
        }

        // Wraps every hook call: cancellation passes through, anything else becomes an
        // AgentHookException so it ends the run and is never mistaken for a streaming failure.
        private static async ValueTask InvokeHookAsync(string hookName, int iteration, Func<ValueTask> call, Exception? toolException = null)
        {
            try
            {
                await call();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AgentHookException(hookName, iteration, ex, toolException);
            }
        }

        // Returns non-null (true) when the iteration loop should stop, null to continue. Neither the
        // Done nor Continue flags of the former tuple were ever consulted by the callers, so the tuple
        // machinery was pure complexity (CR-L002).
        private async Task<bool?> HandleResponse(LlmResponse response, List<Message> conversation, int iteration, int maxIter, IAgentHooks? hooks, CancellationToken cancellationToken = default)
        {
            switch (response.StopReason)
            {
                case "tool_use":
                    return await HandleToolUse(response, conversation, iteration, maxIter, hooks, cancellationToken);

                case "end_turn":
                    foreach (var block in (response.Content ?? Enumerable.Empty<ContentBlock>()).Where(b => b.Type == "text"))
                        SendMessage("assistant_final", block.Text ?? "");
                    return true;

                case "error":
                    var actualError = response.ErrorMessage ?? "Unknown LLM error";
                    SendMessage("error", $"LLM request failed: {actualError}");
                    EnsureErrorContent(response, conversation, $"Error: LLM request failed: {actualError}");
                    return true;

                case "NotConfigured":
                    SendMessage("error", $"Provider '{_llmProvider.Name}' is not properly configured.");
                    EnsureErrorContent(response, conversation, $"Error: Provider '{_llmProvider.Name}' is not properly configured.");
                    return true;

                default:
                    if (_options.Verbose)
                        SendMessage("warning", $"Unexpected stop reason: {response.StopReason ?? "unknown"}. Stopping.");
                    return true;
            }
        }

        private async Task<bool?> HandleToolUse(LlmResponse response, List<Message> conversation, int iteration, int maxIter, IAgentHooks? hooks, CancellationToken cancellationToken = default)
        {
            var toolResults = new List<object>();
            var errorCount = 0;

            foreach (var block in (response.Content ?? Enumerable.Empty<ContentBlock>()).Where(b => b.Type == "tool_use"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var toolName = block.Name ?? "";
                var toolUseId = block.Id ?? "";
                var input = block.Input ?? [];
                var readOnlyInput = new ReadOnlyDictionary<string, object>(input);

                var toolCallMsg = $"Tool: {block.Name}\nInput: {JsonSerializer.Serialize(block.Input)}";
                SendMessage("tool_call", toolCallMsg);

                if (hooks != null)
                    await InvokeHookAsync(nameof(IAgentHooks.OnToolCallStartingAsync), iteration,
                        () => hooks.OnToolCallStartingAsync(new ToolCallStarted(iteration, toolUseId, toolName, readOnlyInput), cancellationToken));

                var tool = _tools.FirstOrDefault(t => t.Name == block.Name);
                var startedAt = Stopwatch.GetTimestamp();
                string? result = null;
                Exception? toolException = null;
                try
                {
                    result = tool != null
                        ? await tool.ExecuteAsync(_options.WorkingDirectory, input, cancellationToken)
                        : $"Error: Unknown tool '{block.Name}'";
                }
                catch (Exception ex) when (hooks != null)
                {
                    toolException = ex;
                }
                var duration = Stopwatch.GetElapsedTime(startedAt);

                var isError = toolException != null || result!.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);

                if (hooks != null)
                {
                    var finished = new ToolCallFinished(iteration, toolUseId, toolName, readOnlyInput, result, tool != null, isError, toolException, duration);
                    await InvokeHookAsync(nameof(IAgentHooks.OnToolCallFinishedAsync), iteration,
                        () => hooks.OnToolCallFinishedAsync(finished, cancellationToken), toolException);
                }

                if (toolException != null)
                    ExceptionDispatchInfo.Capture(toolException).Throw();

                if (isError)
                    errorCount++;

                var preview = result!.Length > 500 ? string.Concat(result.AsSpan(0, 500), "...") : result;
                SendMessage("tool_result", $"Result from {block.Name}:\n{preview}");

                toolResults.Add(new
                {
                    type = "tool_result",
                    tool_use_id = block.Id,
                    content = result
                });
            }

            conversation.Add(new Message { Role = "user", Content = toolResults });

            var hasTextContent = (response.Content ?? []).Any(b => b.Type == "text" && !string.IsNullOrWhiteSpace(b.Text));
            if (hasTextContent && _options.Verbose)
            {
                foreach (var block in (response.Content ?? []).Where(b => b.Type == "text"))
                    SendMessage("assistant", block.Text ?? "");
            }

            if (iteration >= maxIter)
            {
                SendMessage("warning", $"Maximum iterations ({maxIter}) reached. Task may be incomplete.");
                return true;
            }

            // "All tool executions failed" — derived from the error counter tracked in the loop
            // above rather than reflecting over the anonymous toolResults objects (CR-L003).
            if (toolResults.Count > 0 && errorCount == toolResults.Count && _options.Verbose)
                SendMessage("warning", "All tool executions failed. Giving agent one final response...");

            return null; // continue loop
        }

        private static void EnsureErrorContent(LlmResponse response, List<Message> conversation, string errorText)
        {
            if (response.Content == null || response.Content.Count == 0 || !response.Content.Any(b => b.Type == "text"))
            {
                response.Content ??= new List<ContentBlock>();
                response.Content.Add(new ContentBlock { Type = "text", Text = errorText });
                conversation[^1] = new Message { Role = "assistant", Content = response.Content };
            }
        }
    }
}
