using Birko.AI.Models;
using Birko.AI.Tools;
using System.Text;
using System.Text.Json;

namespace Birko.AI.Providers
{
    public class ClaudeProvider : LlmProviderBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string? _baseUrl;

        public override string Name => "Claude";

        public ClaudeProvider(string apiKey, string model, string baseUrl = "https://api.anthropic.com/v1/messages")
        {
            _apiKey = apiKey;
            _model = model;
            _baseUrl = baseUrl;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }

        public override async Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            if (!IsConfigured()) return NotConfigured();

            try
            {
                var payload = BuildRequestPayload(messages, tools, systemPrompt, _model);
                var json = JsonSerializer.Serialize(payload);

                // Use retry logic for transient failures
                var (response, responseJson) = await SendWithRetryAsync(
                    _httpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        return request;
                    },
                    Name);

                if (response == null || responseJson == null)
                {
                    return LlmResponse.Error("Claude: No response received");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorDetail = ExtractErrorFromResponseBody(responseJson) ?? responseJson;
                    var errorMsg = $"Claude API Error ({response.StatusCode}): {errorDetail}";
                    SendMessage("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

                return ParseResponse(responseJson, MessageCallback);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error calling Claude API: {ex.Message}";
                SendMessage("error", errorMsg);
                return LlmResponse.Error(errorMsg);
            }
        }

        protected override bool IsConfigured() => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_model);

        private static object BuildRequestPayload(IEnumerable<Message> messages, IEnumerable<Tool> tools, string systemPrompt, string model)
        {
            var claudeMessages = messages.Select(m =>
            {
                object content = m.Content ?? "";

                // If content is a list of ContentBlocks, convert to Claude format
                if (m.Content is IEnumerable<ContentBlock> blocks)
                {
                    var contentList = new List<Dictionary<string, object?>>();
                    foreach (var b in blocks)
                    {
                        var dict = new Dictionary<string, object?> { { "type", b.Type } };

                        if (b.Type == "text")
                        {
                            dict["text"] = b.Text;
                        }
                        else if (b.Type == "tool_use")
                        {
                            dict["id"] = b.Id;
                            dict["name"] = b.Name;
                            dict["input"] = b.Input;
                        }

                        contentList.Add(dict);
                    }
                    content = contentList;
                }

                return new { role = m.Role, content };
            }).ToList();

            return new
            {
                model,
                max_tokens = 8192,  // Claude max output tokens
                system = systemPrompt,
                messages = claudeMessages,
                tools = tools.Select(t => new { name = t.Name, description = t.Description, input_schema = t.InputSchema }).ToList()
            };
        }

        private static LlmResponse ParseResponse(string responseJson, Action<string, string>? messageCallback = null)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

                // Check for API errors
                if (result.TryGetProperty("error", out var error))
                {
                    var errorType = error.TryGetProperty("type", out var type) ? type.GetString() : "unknown";
                    var errorMessage = error.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
                    var errorMsg = $"Claude API returned error: {errorType} - {errorMessage}";
                    messageCallback?.Invoke("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

                var llmResponse = new LlmResponse
                {
                    StopReason = result.GetProperty("stop_reason").GetString(),
                    Content = []
                };

                foreach (var block in result.GetProperty("content").EnumerateArray())
                {
                    var blockType = block.GetProperty("type").GetString();
                    if (blockType == "text")
                    {
                        llmResponse.Content.Add(new ContentBlock { Type = "text", Text = block.GetProperty("text").GetString() });
                    }
                    else if (blockType == "tool_use")
                    {
                        var inputDict = new Dictionary<string, object>();
                        foreach (var prop in block.GetProperty("input").EnumerateObject())
                        {
                            inputDict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                                ? prop.Value.GetString() ?? string.Empty
                                : prop.Value.ToString();
                        }
                        llmResponse.Content.Add(new ContentBlock
                        {
                            Type = "tool_use",
                            Id = block.GetProperty("id").GetString(),
                            Name = block.GetProperty("name").GetString(),
                            Input = inputDict
                        });
                    }
                }

                // Extract token usage (Claude: input_tokens, output_tokens)
                if (result.TryGetProperty("usage", out var usage))
                {
                    llmResponse.Usage = new TokenUsage
                    {
                        PromptTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
                        CompletionTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0
                    };
                }

                return llmResponse;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error parsing Claude response: {ex.Message}";
                messageCallback?.Invoke("error", errorMsg);
                return LlmResponse.Error(errorMsg);
            }
        }

        public override async Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            if (!IsConfigured())
            {
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                        new InvalidOperationException("Claude provider is not configured")),
                    Error = "Not configured"
                };
            }

            try
            {
                var payload = new
                {
                    model = _model,
                    max_tokens = 8192,
                    system = systemPrompt,
                    messages = BuildClaudeMessages(messages),
                    tools = tools.Select(t => new { name = t.Name, description = t.Description, input_schema = t.InputSchema }).ToList(),
                    stream = true
                };
                var json = JsonSerializer.Serialize(payload);

                var response = await SendStreamingWithRetryAsync(
                    _httpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        return request;
                    },
                    Name);

                if (response == null)
                {
                    return new LlmStreamingResponse
                    {
                        GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                            new HttpRequestException("Failed to connect to Claude API after retries")),
                        Error = "Connection failed"
                    };
                }

                // Create response object that will be populated during streaming
                var streamingResponse = new LlmStreamingResponse
                {
                    IsComplete = false,
                    GetStreamAsync = null! // Will be set below
                };

                streamingResponse.GetStreamAsync = async () =>
                {
                    var stream = await response.Content.ReadAsStreamAsync();
                    return ParseClaudeStreamChunksWithToolCapture(ParseSseStream(stream), streamingResponse);
                };

                return streamingResponse;
            }
            catch (Exception ex)
            {
                SendMessage("error", $"Error calling Claude streaming API: {ex.Message}");
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(ex),
                    Error = ex.Message
                };
            }
        }

        private static List<object> BuildClaudeMessages(IEnumerable<Message> messages)
        {
            var result = new List<object>();
            foreach (var m in messages)
            {
                object content = m.Content ?? "";

                if (m.Content is IEnumerable<ContentBlock> blocks)
                {
                    var contentList = new List<Dictionary<string, object?>>();
                    foreach (var b in blocks)
                    {
                        var dict = new Dictionary<string, object?> { { "type", b.Type } };

                        if (b.Type == "text")
                        {
                            dict["text"] = b.Text;
                        }
                        else if (b.Type == "tool_use")
                        {
                            dict["id"] = b.Id;
                            dict["name"] = b.Name;
                            dict["input"] = b.Input;
                        }

                        contentList.Add(dict);
                    }
                    content = contentList;
                }

                result.Add(new { role = m.Role, content });
            }
            return result;
        }

        private static async IAsyncEnumerable<string> ParseClaudeStreamChunks(IAsyncEnumerable<string> sseChunks)
        {
            await foreach (var chunk in sseChunks)
            {
                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                JsonElement json;
                try
                {
                    json = JsonSerializer.Deserialize<JsonElement>(chunk);
                }
                catch
                {
                    continue;
                }

                var eventType = json.TryGetProperty("type", out var type) ? type.GetString() : null;

                if (eventType == "content_block_delta")
                {
                    if (json.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("type", out var deltaType) &&
                        deltaType.GetString() == "text_delta" &&
                        delta.TryGetProperty("text", out var text))
                    {
                        var textValue = text.GetString();
                        if (!string.IsNullOrEmpty(textValue))
                        {
                            yield return textValue;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Parses Claude streaming chunks and captures tool calls for the FinalResponse.
        /// Yields text chunks for real-time display while accumulating the full response.
        /// </summary>
        private static async IAsyncEnumerable<string> ParseClaudeStreamChunksWithToolCapture(
            IAsyncEnumerable<string> sseChunks,
            LlmStreamingResponse streamingResponse)
        {
            var contentBlocks = new List<ContentBlock>();
            var currentToolUseBlock = (ContentBlock?)null;
            var toolInputBuilder = new StringBuilder();
            var textBuilder = new StringBuilder();
            string? stopReason = null;
            TokenUsage? tokenUsage = null;

            await foreach (var chunk in sseChunks)
            {
                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                JsonElement json;
                try
                {
                    json = JsonSerializer.Deserialize<JsonElement>(chunk);
                }
                catch
                {
                    continue;
                }

                var eventType = json.TryGetProperty("type", out var type) ? type.GetString() : null;

                switch (eventType)
                {
                    case "message_start":
                        // Initial message event contains input token count
                        if (json.TryGetProperty("message", out var msgStart) &&
                            msgStart.TryGetProperty("usage", out var startUsage))
                        {
                            tokenUsage = new TokenUsage
                            {
                                PromptTokens = startUsage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0
                            };
                        }
                        break;

                    case "content_block_start":
                        // New content block starting
                        if (json.TryGetProperty("content_block", out var contentBlock))
                        {
                            var blockType = contentBlock.TryGetProperty("type", out var bt) ? bt.GetString() : null;

                            if (blockType == "tool_use")
                            {
                                // Starting a tool_use block
                                currentToolUseBlock = new ContentBlock
                                {
                                    Type = "tool_use",
                                    Id = contentBlock.TryGetProperty("id", out var id) ? id.GetString() : null,
                                    Name = contentBlock.TryGetProperty("name", out var name) ? name.GetString() : null,
                                    Input = new Dictionary<string, object>()
                                };
                                toolInputBuilder.Clear();
                            }
                            else if (blockType == "text")
                            {
                                // Text block - will be filled by deltas
                            }
                        }
                        break;

                    case "content_block_delta":
                        if (json.TryGetProperty("delta", out var delta))
                        {
                            var deltaType = delta.TryGetProperty("type", out var dt) ? dt.GetString() : null;

                            if (deltaType == "text_delta" && delta.TryGetProperty("text", out var text))
                            {
                                var textValue = text.GetString();
                                if (!string.IsNullOrEmpty(textValue))
                                {
                                    textBuilder.Append(textValue);
                                    yield return textValue;
                                }
                            }
                            else if (deltaType == "input_json_delta" && delta.TryGetProperty("partial_json", out var partialJson))
                            {
                                // Accumulate tool input JSON
                                var jsonChunk = partialJson.GetString();
                                if (!string.IsNullOrEmpty(jsonChunk))
                                {
                                    toolInputBuilder.Append(jsonChunk);
                                }
                            }
                        }
                        break;

                    case "content_block_stop":
                        // Content block finished
                        if (currentToolUseBlock != null)
                        {
                            // Parse accumulated tool input
                            var inputJson = toolInputBuilder.ToString();
                            if (!string.IsNullOrEmpty(inputJson))
                            {
                                try
                                {
                                    var inputElement = JsonSerializer.Deserialize<JsonElement>(inputJson);
                                    var inputDict = new Dictionary<string, object>();
                                    foreach (var prop in inputElement.EnumerateObject())
                                    {
                                        inputDict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                                            ? prop.Value.GetString() ?? string.Empty
                                            : prop.Value.ToString();
                                    }
                                    currentToolUseBlock.Input = inputDict;
                                }
                                catch
                                {
                                    // If parsing fails, store raw JSON
                                    currentToolUseBlock.Input = new Dictionary<string, object> { ["_raw"] = inputJson };
                                }
                            }
                            contentBlocks.Add(currentToolUseBlock);
                            currentToolUseBlock = null;
                            toolInputBuilder.Clear();
                        }
                        break;

                    case "message_delta":
                        // Message-level delta, contains stop_reason and output token usage
                        if (json.TryGetProperty("delta", out var msgDelta) &&
                            msgDelta.TryGetProperty("stop_reason", out var sr))
                        {
                            stopReason = sr.GetString();
                        }
                        if (json.TryGetProperty("usage", out var deltaUsage))
                        {
                            var outputTokens = deltaUsage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
                            if (tokenUsage != null)
                                tokenUsage.CompletionTokens = outputTokens;
                            else
                                tokenUsage = new TokenUsage { CompletionTokens = outputTokens };
                        }
                        break;

                    case "message_stop":
                        // Message complete
                        break;
                }
            }

            // Build final response with all content
            var finalContent = new List<ContentBlock>();

            // Add accumulated text as a content block
            var accumulatedText = textBuilder.ToString();
            if (!string.IsNullOrEmpty(accumulatedText))
            {
                finalContent.Add(new ContentBlock { Type = "text", Text = accumulatedText });
            }

            // Add any tool_use blocks
            finalContent.AddRange(contentBlocks);

            // Populate the streaming response with final data
            streamingResponse.FinalResponse = new LlmResponse
            {
                StopReason = stopReason ?? "end_turn",
                Content = finalContent,
                Usage = tokenUsage
            };
            streamingResponse.StopReason = stopReason ?? "end_turn";
            streamingResponse.AccumulatedText = accumulatedText;
            streamingResponse.Usage = tokenUsage;
            streamingResponse.IsComplete = true;
        }
    }
}
