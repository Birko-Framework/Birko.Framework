using Birko.AI.Models;
using Birko.AI.Tools;
using System.Text;
using System.Text.Json;

namespace Birko.AI.Providers
{
    /// <summary>
    /// Provider for Z.AI (formerly Zhipu AI) GLM models.
    /// Z.AI offers OpenAI-compatible API with GLM-4.5, GLM-4.6, GLM-4.7, GLM-4.8 models.
    /// API endpoint: https://api.z.ai/api/paas/v4/chat/completions
    /// China endpoint: https://open.bigmodel.cn/api/paas/v4/chat/completions
    /// </summary>
    public class ZAiProvider : LlmProviderBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;
        private readonly bool _enableDeepThinking;

        /// <summary>
        /// International API endpoint
        /// </summary>
        public const string InternationalEndpoint = "https://api.z.ai/api/paas/v4";

        /// <summary>
        /// International Coding API endpoint (for coding scenarios only)
        /// </summary>
        public const string InternationalCodingEndpoint = "https://api.z.ai/api/coding/paas/v4";

        /// <summary>
        /// China mainland API endpoint
        /// </summary>
        public const string ChinaEndpoint = "https://open.bigmodel.cn/api/paas/v4";

        /// <summary>
        /// Default recommended model (latest stable)
        /// </summary>
        public const string DefaultModel = Models.Glm51;

        public override string Name => $"Z.AI ({_model})";

        /// <summary>
        /// Creates a new Z.AI provider
        /// </summary>
        /// <param name="apiKey">Z.AI API key (ZHIPU_API_KEY)</param>
        /// <param name="model">Model name (defaults to latest recommended model)</param>
        /// <param name="baseUrl">API base URL (defaults to international endpoint)</param>
        /// <param name="enableDeepThinking">Enable Deep Thinking mode for supported models</param>
        /// <param name="useCodingEndpoint">Use coding-optimized endpoint for code generation tasks</param>
        public ZAiProvider(
            string apiKey,
            string? model = null,
            string? baseUrl = null,
            bool enableDeepThinking = false,
            bool useCodingEndpoint = false)
        {
            _apiKey = apiKey;
            _model = model ?? DefaultModel;

            // Validate model
            ValidateModel(_model);

            // Select appropriate base URL based on coding endpoint flag
            if (baseUrl != null)
            {
                _baseUrl = baseUrl.TrimEnd('/');
            }
            else
            {
                _baseUrl = useCodingEndpoint ? InternationalCodingEndpoint : InternationalEndpoint;
            }

            _enableDeepThinking = enableDeepThinking;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public override async Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured()) return NotConfigured();

            try
            {
                var payload = BuildPayload(messages, tools, systemPrompt);
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                var url = $"{_baseUrl}/chat/completions";

                SendMessage("debug", $"Z.AI request to {url}");

                // Use retry logic for transient failures
                var (response, responseJson) = await SendWithRetryAsync(
                    _httpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, url);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        return request;
                    },
                    Name,
                    cancellationToken);

                if (response == null || responseJson == null)
                {
                    return LlmResponse.Error("Z.AI: No response received");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorDetail = ExtractErrorFromResponseBody(responseJson) ?? responseJson;
                    var errorMsg = $"Z.AI API Error ({response.StatusCode}): {errorDetail}";
                    SendMessage("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

                return ParseResponse(responseJson);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error calling Z.AI API: {ex.Message}";
                SendMessage("error", errorMsg);
                return LlmResponse.Error(errorMsg);
            }
        }

        protected override bool IsConfigured() => !string.IsNullOrWhiteSpace(_apiKey);

        private object BuildPayload(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            var openAiMessages = BuildOpenAiStyleMessages(messages, systemPrompt);
            var openAiTools = BuildOpenAiStyleTools(tools);

            // Base payload
            var payload = new Dictionary<string, object?>
            {
                ["model"] = _model,
                ["messages"] = openAiMessages,
                ["stream"] = false,
                ["max_tokens"] = GetMaxTokensForModel(_model),
                ["temperature"] = 0.7
            };

            // Add tools if any
            if (tools.Any())
            {
                payload["tools"] = openAiTools;
            }

            // Add Deep Thinking mode if enabled (for supported models like GLM-4.5+)
            if (_enableDeepThinking)
            {
                payload["thinking"] = new Dictionary<string, object>
                {
                    ["type"] = "enabled"
                };
            }

            return payload;
        }

        /// <summary>
        /// Get appropriate max_tokens based on model
        /// </summary>
        private static int GetMaxTokensForModel(string model)
        {
            return model switch
            {
                Models.Glm51Turbo => 131072,    // GLM-5.1 Turbo supports up to 131072 tokens
                Models.Glm51 => 131072,         // GLM-5.1 supports up to 131072 tokens
                Models.Glm5Turbo => 128000,     // GLM-5 Turbo supports up to 128000 tokens
                Models.Glm5 => 128000,          // GLM-5 supports up to 128000 tokens
                Models.Glm47 => 8192,           // GLM-4.7 supports up to 8K output
                Models.Glm48 => 8192,           // GLM-4.8 supports up to 8K output
                Models.Glm4VPlus => 4096,       // Vision models typically have lower output limits
                Models.Glm4V => 4096,
                _ => 4096                       // Default for other models
            };
        }

        private LlmResponse ParseResponse(string responseJson)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
                var llmResponse = new LlmResponse { Content = [] };

                // Check for errors
                if (result.TryGetProperty("error", out var error))
                {
                    var errorMessage = error.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
                    var errorMsg = $"Z.AI returned error: {errorMessage}";
                    SendMessage("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

                // Standard OpenAI-compatible format
                if (!result.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    SendMessage("error", "Invalid response format: missing choices array");
                    return LlmResponse.Error("Z.AI: Invalid response format - missing choices array");
                }

                var choice = choices[0];
                var message = choice.GetProperty("message");

                // Check for tool calls
                if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    llmResponse.StopReason = "tool_use";
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        var function = toolCall.GetProperty("function");
                        var argumentsJson = function.GetProperty("arguments").GetString();
                        Dictionary<string, object>? args = null;

                        if (argumentsJson is not null)
                        {
                            try
                            {
                                args = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson);
                            }
                            catch (JsonException ex)
                            {
                                SendMessage("error", $"Failed to parse tool arguments: {ex.Message}. Arguments JSON: {argumentsJson?.Substring(0, Math.Min(500, argumentsJson.Length))}");
                                args = new Dictionary<string, object>();
                            }
                        }
                        else
                        {
                            SendMessage("warning", "Tool call has null arguments");
                        }

                        llmResponse.Content.Add(new ContentBlock
                        {
                            Type = "tool_use",
                            Id = toolCall.TryGetProperty("id", out var idProp) ? idProp.GetString() : Guid.NewGuid().ToString(),
                            Name = function.GetProperty("name").GetString(),
                            Input = args ?? new Dictionary<string, object>()
                        });
                    }
                }
                else
                {
                    // Regular text response
                    llmResponse.StopReason = "end_turn";

                    // Check for thinking/reasoning content (multiple possible field names for compatibility)
                    var thinkingFields = new[] { "thinking_content", "reasoning_content", "thinking" };
                    foreach (var fieldName in thinkingFields)
                    {
                        if (message.TryGetProperty(fieldName, out var thinkingContent))
                        {
                            var thinking = thinkingContent.GetString();
                            if (!string.IsNullOrEmpty(thinking))
                            {
                                SendMessage("thinking", thinking);
                            }
                            break;
                        }
                    }

                    if (message.TryGetProperty("content", out var textContent))
                    {
                        var text = textContent.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            llmResponse.Content.Add(new ContentBlock { Type = "text", Text = text });
                        }
                    }
                }

                // Extract token usage
                if (result.TryGetProperty("usage", out var usage))
                {
                    llmResponse.Usage = new TokenUsage
                    {
                        PromptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                        CompletionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0,
                        Model = result.TryGetProperty("model", out var mdl) ? mdl.GetString() : _model
                    };
                }

                return llmResponse;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error parsing Z.AI response: {ex.Message}";
                SendMessage("error", errorMsg);
                return LlmResponse.Error(errorMsg);
            }
        }

        /// <summary>
        /// Validate model name and warn if unknown
        /// </summary>
        private void ValidateModel(string model)
        {
            if (!ValidModels.Contains(model))
            {
                SendMessage("warning", $"Model '{model}' may not be recognized. Known models: {string.Join(", ", ValidModels.Take(5))}...");
            }
        }

        private static readonly HashSet<string> ValidModels = new()
        {
            // GLM-5 series
            Models.Glm5, Models.Glm5Turbo, Models.Glm51, Models.Glm51Turbo,
            // GLM-4.5 series
            Models.Glm45Flash, Models.Glm45Air, Models.Glm45,
            // GLM-4.6 series
            Models.Glm46Flash,
            // GLM-4.7 & GLM-4.8 series (latest)
            Models.Glm47, Models.Glm48, Models.Glm48Flash,
            // Vision models
            Models.Glm4VPlus, Models.Glm4V,
            // Code models
            Models.CodeGeeX4,
            // Legacy models
            Models.Glm4Plus, Models.Glm4, Models.Glm4Air, Models.Glm4Flash
        };

        /// <summary>
        /// Available Z.AI models (updated for latest versions)
        /// </summary>
        public static class Models
        {
            // GLM-5 series
            public const string Glm51Turbo = "glm-5.1-turbo";
            public const string Glm51 = "glm-5.1";
            public const string Glm5Turbo = "glm-5-turbo";
            public const string Glm5 = "glm-5";
            // GLM-4.5 series
            public const string Glm45Flash = "glm-4.5-flash";
            public const string Glm45Air = "glm-4.5-air";
            public const string Glm45 = "glm-4.5";

            // GLM-4.6 series
            public const string Glm46Flash = "glm-4.6-flash";

            // GLM-4.7 series (200K context) - Current flagship
            public const string Glm47 = "glm-4.7";

            // GLM-4.8 series (latest)
            public const string Glm48 = "glm-4.8";
            public const string Glm48Flash = "glm-4.8-flash";

            // Vision models
            public const string Glm4VPlus = "glm-4v-plus";
            public const string Glm4V = "glm-4v";

            // Code models
            public const string CodeGeeX4 = "codegeex-4";

            // Legacy models
            public const string Glm4Plus = "glm-4-plus";
            public const string Glm4 = "glm-4";
            public const string Glm4Air = "glm-4-air";
            public const string Glm4Flash = "glm-4-flash";
        }

        public override async Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured())
            {
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                        new InvalidOperationException("Z.AI provider is not configured")),
                    Error = "Not configured"
                };
            }

            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["model"] = _model,
                    ["messages"] = BuildOpenAiStyleMessages(messages, systemPrompt),
                    ["stream"] = true,
                    ["max_tokens"] = GetMaxTokensForModel(_model)
                };

                if (tools != null && tools.Count > 0)
                {
                    payload["tools"] = BuildOpenAiStyleTools(tools);
                }

                if (_enableDeepThinking)
                {
                    payload["thinking"] = new Dictionary<string, object> { ["type"] = "enabled" };
                }

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                var url = $"{_baseUrl}/chat/completions";

                var response = await SendStreamingWithRetryAsync(
                    _httpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, url);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        return request;
                    },
                    Name,
                    cancellationToken);

                if (response == null)
                {
                    return new LlmStreamingResponse
                    {
                        GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                            new HttpRequestException("Failed to connect to Z.AI API after retries")),
                        Error = "Connection failed"
                    };
                }

                // Create response object that will be populated during streaming
                var streamingResponse = new LlmStreamingResponse
                {
                    IsComplete = false,
                    GetStreamAsync = null! // Will be set below
                };
                streamingResponse.Resource = response;

                streamingResponse.GetStreamAsync = async () =>
                {
                    var stream = await response.Content.ReadAsStreamAsync();
                    var sseStream = ParseSseStream(stream, cancellationToken);
                    return ParseOpenAiStreamChunksWithToolCapture(sseStream, streamingResponse, cancellationToken);
                };

                return streamingResponse;
            }
            catch (Exception ex)
            {
                SendMessage("error", $"Error calling Z.AI streaming API: {ex.Message}");
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(ex),
                    Error = ex.Message
                };
            }
        }
    }
}
