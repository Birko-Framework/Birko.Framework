using Birko.AI.Models;
using Birko.AI.Tools;
using System.Text;
using System.Text.Json;

namespace Birko.AI.Providers
{
    public class GeminiProvider : LlmProviderBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string? _baseUrl;

        public override string Name => "Gemini";

        public GeminiProvider(string apiKey, string model = "gemini-2.0-flash-exp", string baseUrl = "https://generativelanguage.googleapis.com/v1beta/models/")
        {
            _apiKey = apiKey;
            _model = model;
            _httpClient = new HttpClient();
            _baseUrl = baseUrl;
        }

        public override async Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            if (!IsConfigured()) return NotConfigured();

            try
            {
                var payload = BuildRequestPayload(messages, tools, systemPrompt);
                var json = JsonSerializer.Serialize(payload);
                var url = $"{_baseUrl}{_model}:generateContent?key={_apiKey}";

                // Use retry logic for transient failures
                var (response, responseJson) = await SendWithRetryAsync(
                    _httpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, url);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        return request;
                    },
                    Name);

                if (response == null || responseJson == null)
                {
                    return LlmResponse.Error("Gemini: No response received");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorDetail = ExtractErrorFromResponseBody(responseJson) ?? responseJson;
                    var errorMsg = $"Gemini API Error ({response.StatusCode}): {errorDetail}";
                    SendMessage("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

                return ParseResponse(responseJson, MessageCallback);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error calling Gemini API: {ex.Message}";
                SendMessage("error", errorMsg);
                return LlmResponse.Error(errorMsg);
            }
        }

        protected override bool IsConfigured() => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_model);

        private static object BuildRequestPayload(IEnumerable<Message> messages, IEnumerable<Tool> tools, string systemPrompt)
        {
            var contents = new List<object>();

            foreach (var message in messages)
            {
                var role = message.Role == "assistant" ? "model" : "user";
                var parts = new List<object>();

                // Handle different content types
                if (message.Content is string textContent)
                {
                    parts.Add(new { text = textContent });
                }
                else if (message.Content is IEnumerable<ContentBlock> blocks)
                {
                    // Handle ContentBlock objects from assistant or parsed responses
                    foreach (var block in blocks)
                    {
                        if (block.Type == "text" && !string.IsNullOrEmpty(block.Text))
                        {
                            parts.Add(new { text = block.Text });
                        }
                        else if (block.Type == "tool_use" && block.Name != null)
                        {
                            // Gemini represents tool calls as functionCall parts
                            parts.Add(new
                            {
                                functionCall = new
                                {
                                    name = block.Name,
                                    args = block.Input ?? new Dictionary<string, object>()
                                }
                            });
                        }
                    }
                }
                else if (message.Content is List<object> contentList)
                {
                    // Tool results from agent
                    foreach (var item in contentList)
                    {
                        if (item is JsonElement jsonElement)
                        {
                            if (jsonElement.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "tool_result")
                            {
                                var toolUseId = jsonElement.GetProperty("tool_use_id").GetString();
                                var resultContent = jsonElement.GetProperty("content").GetString();

                                parts.Add(new
                                {
                                    functionResponse = new
                                    {
                                        name = toolUseId,
                                        response = new { result = resultContent }
                                    }
                                });
                            }
                        }
                        else
                        {
                            // Check if it's an anonymous object with type property
                            var itemType = item.GetType();
                            var typeProp = itemType.GetProperty("type");

                            if (typeProp != null)
                            {
                                var typeValue = typeProp.GetValue(item)?.ToString();
                                if (typeValue == "tool_result")
                                {
                                    var toolUseIdProp = itemType.GetProperty("tool_use_id");
                                    var contentProp = itemType.GetProperty("content");

                                    if (toolUseIdProp != null && contentProp != null)
                                    {
                                        var toolUseId = toolUseIdProp.GetValue(item)?.ToString();
                                        var resultContent = contentProp.GetValue(item)?.ToString();

                                        parts.Add(new
                                        {
                                            functionResponse = new
                                            {
                                                name = toolUseId,
                                                response = new { result = resultContent }
                                            }
                                        });
                                    }
                                }
                            }
                            else
                            {
                                // Fallback: convert to string
                                parts.Add(new { text = item?.ToString() ?? string.Empty });
                            }
                        }
                    }
                }
                else if (message.Content != null)
                {
                    // Fallback for unknown types
                    parts.Add(new { text = message.Content.ToString() ?? string.Empty });
                }

                if (parts.Count > 0)
                {
                    contents.Add(new { role, parts = parts.ToArray() });
                }
            }

            var payload = new
            {
                contents = contents.ToArray(),
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                generationConfig = new
                {
                    maxOutputTokens = 8192  // Gemini 2.0 max output tokens
                }
            };

            // Only add tools if there are any
            if (tools.Any())
            {
                return new
                {
                    contents = payload.contents,
                    systemInstruction = payload.systemInstruction,
                    generationConfig = payload.generationConfig,
                    tools = new[]
                    {
                        new
                        {
                            functionDeclarations = tools.Select(t => new
                            {
                                name = t.Name,
                                description = t.Description,
                                parameters = t.InputSchema
                            }).ToArray()
                        }
                    }
                };
            }

            return payload;
        }

        private static LlmResponse ParseResponse(string responseJson, Action<string, string>? messageCallback = null)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
                var llmResponse = new LlmResponse { Content = [] };

                // Check for API errors
                if (result.TryGetProperty("error", out var error))
                {
                    var errorMessage = error.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
                    var errorMsg = $"Gemini API returned error: {errorMessage}";
                    messageCallback?.Invoke("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

                if (result.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];

                    // Check finish reason
                    if (candidate.TryGetProperty("finishReason", out var finishReason))
                    {
                        var reason = finishReason.GetString();
                        if (reason == "SAFETY" || reason == "RECITATION")
                        {
                            var errorMsg = $"Gemini blocked response: {reason}";
                            messageCallback?.Invoke("error", errorMsg);
                            return LlmResponse.Error(errorMsg);
                        }
                    }

                    if (candidate.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts))
                    {
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textElement))
                            {
                                llmResponse.StopReason = "end_turn";
                                llmResponse.Content.Add(new ContentBlock { Type = "text", Text = textElement.GetString() });
                            }
                            else if (part.TryGetProperty("functionCall", out var functionCall))
                            {
                                llmResponse.StopReason = "tool_use";
                                var args = new Dictionary<string, object>();
                                if (functionCall.TryGetProperty("args", out var argsElement))
                                {
                                    foreach (var prop in argsElement.EnumerateObject())
                                    {
                                        args[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                                            ? prop.Value.GetString() ?? string.Empty
                                            : prop.Value.ToString();
                                    }
                                }
                                llmResponse.Content.Add(new ContentBlock
                                {
                                    Type = "tool_use",
                                    Id = Guid.NewGuid().ToString(),
                                    Name = functionCall.GetProperty("name").GetString(),
                                    Input = args
                                });
                            }
                        }
                    }
                }

                // If no stop reason was set, default to end_turn
                if (string.IsNullOrEmpty(llmResponse.StopReason))
                {
                    llmResponse.StopReason = "end_turn";
                }

                // Extract token usage (Gemini: usageMetadata)
                if (result.TryGetProperty("usageMetadata", out var usageMeta))
                {
                    llmResponse.Usage = new TokenUsage
                    {
                        PromptTokens = usageMeta.TryGetProperty("promptTokenCount", out var ptc) ? ptc.GetInt32() : 0,
                        CompletionTokens = usageMeta.TryGetProperty("candidatesTokenCount", out var ctc) ? ctc.GetInt32() : 0
                    };
                }

                return llmResponse;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error parsing Gemini response: {ex.Message}";
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
                        new InvalidOperationException("Gemini provider is not configured")),
                    Error = "Not configured"
                };
            }

            try
            {
                var payload = BuildRequestPayload(messages, tools, systemPrompt);
                var json = JsonSerializer.Serialize(payload);
                var url = $"{_baseUrl}{_model}:streamGenerateContent?alt=sse&key={_apiKey}";

                var response = await SendStreamingWithRetryAsync(
                    _httpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, url);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        return request;
                    },
                    Name);

                if (response == null)
                {
                    return new LlmStreamingResponse
                    {
                        GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                            new HttpRequestException("Failed to connect to Gemini API after retries")),
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
                    return ParseGeminiStreamChunksWithToolCapture(ParseSseStream(stream), streamingResponse);
                };

                return streamingResponse;
            }
            catch (Exception ex)
            {
                SendMessage("error", $"Error calling Gemini streaming API: {ex.Message}");
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(ex),
                    Error = ex.Message
                };
            }
        }

        private static async IAsyncEnumerable<string> ParseGeminiStreamChunks(IAsyncEnumerable<string> sseChunks)
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

                if (!json.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                    continue;

                var candidate = candidates[0];
                if (!candidate.TryGetProperty("content", out var content))
                    continue;

                if (!content.TryGetProperty("parts", out var parts))
                    continue;

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text))
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
        /// Parses Gemini streaming chunks with tool call capture.
        /// Yields text chunks for real-time display while accumulating tool calls.
        /// </summary>
        private static async IAsyncEnumerable<string> ParseGeminiStreamChunksWithToolCapture(
            IAsyncEnumerable<string> sseChunks,
            LlmStreamingResponse streamingResponse)
        {
            var textBuilder = new System.Text.StringBuilder();
            var toolCalls = new List<ContentBlock>();
            string? finishReason = null;
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

                // Capture usage metadata from any chunk (Gemini includes it in the final chunk)
                if (json.TryGetProperty("usageMetadata", out var usageMeta))
                {
                    tokenUsage = new TokenUsage
                    {
                        PromptTokens = usageMeta.TryGetProperty("promptTokenCount", out var ptc) ? ptc.GetInt32() : 0,
                        CompletionTokens = usageMeta.TryGetProperty("candidatesTokenCount", out var ctc) ? ctc.GetInt32() : 0
                    };
                }

                if (!json.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                    continue;

                var candidate = candidates[0];

                // Capture finish reason
                if (candidate.TryGetProperty("finishReason", out var fr))
                {
                    finishReason = fr.GetString();
                }

                if (!candidate.TryGetProperty("content", out var content))
                    continue;

                if (!content.TryGetProperty("parts", out var parts))
                    continue;

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text))
                    {
                        var textValue = text.GetString();
                        if (!string.IsNullOrEmpty(textValue))
                        {
                            textBuilder.Append(textValue);
                            yield return textValue;
                        }
                    }
                    else if (part.TryGetProperty("functionCall", out var functionCall))
                    {
                        // Capture tool call
                        var args = new Dictionary<string, object>();
                        if (functionCall.TryGetProperty("args", out var argsElement))
                        {
                            foreach (var prop in argsElement.EnumerateObject())
                            {
                                args[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                                    ? prop.Value.GetString() ?? string.Empty
                                    : prop.Value.ToString();
                            }
                        }

                        toolCalls.Add(new ContentBlock
                        {
                            Type = "tool_use",
                            Id = Guid.NewGuid().ToString(),
                            Name = functionCall.TryGetProperty("name", out var name) ? name.GetString() : null,
                            Input = args
                        });
                    }
                }
            }

            // Build final response
            var finalContent = new List<ContentBlock>();

            // Add accumulated text
            var accumulatedText = textBuilder.ToString();
            if (!string.IsNullOrEmpty(accumulatedText))
            {
                finalContent.Add(new ContentBlock { Type = "text", Text = accumulatedText });
            }

            // Add tool calls
            finalContent.AddRange(toolCalls);

            // Determine stop reason
            string stopReason;
            if (toolCalls.Count > 0)
            {
                stopReason = "tool_use";
            }
            else if (finishReason == "SAFETY" || finishReason == "RECITATION")
            {
                stopReason = "error";
            }
            else
            {
                stopReason = "end_turn";
            }

            // Populate streaming response
            streamingResponse.FinalResponse = new LlmResponse
            {
                StopReason = stopReason,
                Content = finalContent,
                Usage = tokenUsage
            };
            streamingResponse.StopReason = stopReason;
            streamingResponse.AccumulatedText = accumulatedText;
            streamingResponse.Usage = tokenUsage;
            streamingResponse.IsComplete = true;
        }
    }
}
