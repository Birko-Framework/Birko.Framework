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

        private async Task<List<Message>> RunWithHistoryAsync(List<Message> conversation, int? maxIterations = null, CancellationToken cancellationToken = default)
        {
            if (_options.EnableStreaming)
            {
                try
                {
                    return await RunWithHistoryStreamingAsync(conversation, maxIterations, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && _options.StreamingFallbackToSync)
                {
                    SendMessage("warning", $"Streaming failed: {ex.Message}. Falling back to synchronous mode.");
                }
            }

            return await RunWithHistorySyncAsync(conversation, maxIterations, cancellationToken);
        }

        private async Task<List<Message>> RunWithHistoryStreamingAsync(List<Message> conversation, int? maxIterations = null, CancellationToken cancellationToken = default)
        {
            var maxIter = maxIterations ?? _options.MaxIterations;
            var iteration = 1;

            while (iteration <= maxIter)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_options.Verbose)
                    SendMessage("info", $"ITERATION {iteration}{(iteration == 1 ? " (streaming)" : "")}");

                LlmResponse response;

                if (iteration == 1)
                {
                    // await using disposes the streaming response — and its underlying HTTP
                    // connection (LlmStreamingResponse.Resource) — even if enumeration is abandoned
                    // early by an exception or cancellation (CR-M003).
                    await using var streamingResponse = await _llmProvider.SendMessageStreamingAsync(conversation, _tools, SystemPrompt, cancellationToken);
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

                    response = streamingResponse.FinalResponse ?? new LlmResponse
                    {
                        StopReason = streamingResponse.StopReason ?? "end_turn",
                        Content = new List<ContentBlock>
                        {
                            new ContentBlock { Type = "text", Text = fullText.ToString() }
                        }
                    };
                }
                else
                {
                    response = await _llmProvider.SendMessageAsync(conversation, _tools, SystemPrompt, cancellationToken);
                    _options.OnLlmResponseReceived?.Invoke();
                }

                if (_options.Verbose)
                    SendMessage("info", $"Stop reason: {response.StopReason}");

                conversation.Add(new Message { Role = "assistant", Content = response.Content });

                var result = await HandleResponse(response, conversation, iteration, maxIter, cancellationToken);
                if (result.HasValue)
                    return result.Value.Done ? conversation : conversation;

                iteration++;
            }

            if (_options.Verbose)
                SendMessage("warning", $"Maximum iterations ({maxIter}) reached.");

            return conversation;
        }

        private async Task<List<Message>> RunWithHistorySyncAsync(List<Message> conversation, int? maxIterations = null, CancellationToken cancellationToken = default)
        {
            var maxIter = maxIterations ?? _options.MaxIterations;

            for (int iteration = 1; iteration <= maxIter; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_options.Verbose)
                    SendMessage("info", $"ITERATION {iteration}");

                var response = await _llmProvider.SendMessageAsync(conversation, _tools, SystemPrompt, cancellationToken);
                _options.OnLlmResponseReceived?.Invoke();

                if (_options.Verbose)
                    SendMessage("info", $"Stop reason: {response.StopReason}");

                conversation.Add(new Message { Role = "assistant", Content = response.Content });

                var result = await HandleResponse(response, conversation, iteration, maxIter, cancellationToken);
                if (result.HasValue)
                    return conversation;
            }

            if (_options.Verbose)
                SendMessage("warning", $"Maximum iterations ({maxIter}) reached.");

            return conversation;
        }

        private async Task<(bool Done, bool Continue)?> HandleResponse(LlmResponse response, List<Message> conversation, int iteration, int maxIter, CancellationToken cancellationToken = default)
        {
            switch (response.StopReason)
            {
                case "tool_use":
                    return await HandleToolUse(response, conversation, iteration, maxIter, cancellationToken);

                case "end_turn":
                    foreach (var block in (response.Content ?? Enumerable.Empty<ContentBlock>()).Where(b => b.Type == "text"))
                        SendMessage("assistant_final", block.Text ?? "");
                    return (Done: true, Continue: false);

                case "error":
                    var actualError = response.ErrorMessage ?? "Unknown LLM error";
                    SendMessage("error", $"LLM request failed: {actualError}");
                    EnsureErrorContent(response, conversation, $"Error: LLM request failed: {actualError}");
                    return (Done: true, Continue: false);

                case "NotConfigured":
                    SendMessage("error", $"Provider '{_llmProvider.Name}' is not properly configured.");
                    EnsureErrorContent(response, conversation, $"Error: Provider '{_llmProvider.Name}' is not properly configured.");
                    return (Done: true, Continue: false);

                default:
                    if (_options.Verbose)
                        SendMessage("warning", $"Unexpected stop reason: {response.StopReason ?? "unknown"}. Stopping.");
                    return (Done: true, Continue: false);
            }
        }

        private async Task<(bool Done, bool Continue)?> HandleToolUse(LlmResponse response, List<Message> conversation, int iteration, int maxIter, CancellationToken cancellationToken = default)
        {
            var toolResults = new List<object>();
            var hasErrors = false;

            foreach (var block in (response.Content ?? Enumerable.Empty<ContentBlock>()).Where(b => b.Type == "tool_use"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var toolCallMsg = $"Tool: {block.Name}\nInput: {JsonSerializer.Serialize(block.Input)}";
                SendMessage("tool_call", toolCallMsg);

                var tool = _tools.FirstOrDefault(t => t.Name == block.Name);
                var result = tool != null
                    ? await tool.ExecuteAsync(_options.WorkingDirectory, block.Input ?? [], cancellationToken)
                    : $"Error: Unknown tool '{block.Name}'";

                if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                    hasErrors = true;

                var preview = result.Length > 500 ? string.Concat(result.AsSpan(0, 500), "...") : result;
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
                return (Done: true, Continue: false);
            }

            if (hasErrors && toolResults.Count > 0 && toolResults.All(r =>
                r.GetType().GetProperty("content")?.GetValue(r)?.ToString()?.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                if (_options.Verbose)
                    SendMessage("warning", "All tool executions failed. Giving agent one final response...");
            }

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
