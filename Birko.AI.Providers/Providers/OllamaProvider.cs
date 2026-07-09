using Birko.AI.Models;
using Birko.AI.Tools;
using System.Text;
using System.Text.Json;

namespace Birko.AI.Providers
{
    public class OllamaProvider : LlmProviderBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _model;
        private readonly string _baseUrl;

        public override string Name => $"Ollama ({_model})";

        public OllamaProvider(string model = "llama3.2", string baseUrl = "http://localhost:11434", HttpMessageHandler? handler = null)
        {
            _model = model;
            _baseUrl = baseUrl.TrimEnd('/');
            // handler is a test seam (inject a fake HttpMessageHandler); production callers omit it.
            _httpClient = new HttpClient(handler ?? new HttpClientHandler())
            {
                Timeout = TimeSpan.FromMinutes(5) // Local models can be slow
            };
        }

        public override async Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured()) return NotConfigured();

            try
            {
                var payload = new
                {
                    model = _model,
                    messages = BuildOpenAiStyleMessages(messages, systemPrompt),
                    stream = false,
                    tools = BuildOpenAiStyleTools(tools),
                    num_predict = -1  // Ollama's parameter name for max_tokens (-1 = unlimited)
                };
                var json = JsonSerializer.Serialize(payload);
                var url = $"{_baseUrl}/api/chat";

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
                    return LlmResponse.Error("Ollama: No response received");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorDetail = ExtractErrorFromResponseBody(responseJson) ?? responseJson;
                    var errorMsg = $"Ollama API Error ({response.StatusCode}): {errorDetail}";
                    SendMessage("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

                return ParseResponse(responseJson);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error calling Ollama API: {ex.Message}";
                SendMessage("error", errorMsg);
                return LlmResponse.Error(errorMsg);
            }
        }

        protected override bool IsConfigured() => !string.IsNullOrWhiteSpace(_model);

        private static List<object> BuildMessages(IEnumerable<Message> messages, string systemPrompt)
        {
            var list = new List<object> { new { role = "system", content = systemPrompt } };
            list.AddRange(messages.Select(m => new { role = m.Role, content = m.Content }));
            return list;
        }

        private static object BuildRequestPayload(IEnumerable<Message> messages, IEnumerable<Tool> tools, string systemPrompt) => new
        {
            model = "" /* replaced below */,
            messages = BuildMessages(messages, systemPrompt),
            stream = false,
            tools = tools.Select(t => new { type = "function", function = new { name = t.Name, description = t.Description, parameters = t.InputSchema } }).ToList()
        };

        private static LlmResponse ParseResponse(string responseJson)
        {
            var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
            var message = result.GetProperty("message");
            var llmResponse = new LlmResponse { Content = [] };

            if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
            {
                llmResponse.StopReason = "tool_use";
                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    var function = toolCall.GetProperty("function");
                    var argumentsJson = function.GetProperty("arguments").GetString();
                    var args = argumentsJson is not null ? JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson) : [];
                    llmResponse.Content.Add(new ContentBlock
                    {
                        Type = "tool_use",
                        Id = Guid.NewGuid().ToString(),
                        Name = function.GetProperty("name").GetString(),
                        Input = args
                    });
                }
            }
            else
            {
                llmResponse.StopReason = "end_turn";
                if (message.TryGetProperty("content", out var textContent))
                {
                    llmResponse.Content.Add(new ContentBlock { Type = "text", Text = textContent.GetString() });
                }
            }

            return llmResponse;
        }

        public override async Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured())
            {
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                        new InvalidOperationException("Ollama provider is not configured")),
                    Error = "Not configured"
                };
            }

            try
            {
                var payload = new
                {
                    model = _model,
                    messages = BuildOpenAiStyleMessages(messages, systemPrompt),
                    tools = BuildOpenAiStyleTools(tools),
                    stream = true,
                    num_predict = -1  // match the non-streaming path (Ollama's max_tokens; -1 = unlimited)
                };
                var json = JsonSerializer.Serialize(payload);
                // POST to the chat endpoint, not the server root — the streaming path previously used
                // _baseUrl directly, so every streaming call hit e.g. http://localhost:11434 (404). (CR-C01)
                var url = $"{_baseUrl}/api/chat";

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
                            new HttpRequestException("Failed to connect to Ollama API after retries")),
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
                    return ParseOllamaStreamChunksWithToolCapture(stream, streamingResponse, cancellationToken);
                };

                return streamingResponse;
            }
            catch (Exception ex)
            {
                SendMessage("error", $"Error calling Ollama streaming API: {ex.Message}");
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(ex),
                    Error = ex.Message
                };
            }
        }

        private static async IAsyncEnumerable<string> ParseOllamaStreamChunks(Stream stream)
        {
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                JsonElement json;
                try
                {
                    json = JsonSerializer.Deserialize<JsonElement>(line);
                }
                catch
                {
                    continue;
                }

                if (json.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    var text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return text;
                    }
                }

                if (json.TryGetProperty("done", out var done) && done.GetBoolean())
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// Parses Ollama streaming chunks with tool call capture.
        /// Ollama uses NDJSON format with message.content for text and message.tool_calls for tools.
        /// </summary>
        private static async IAsyncEnumerable<string> ParseOllamaStreamChunksWithToolCapture(
            Stream stream,
            LlmStreamingResponse streamingResponse,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var textBuilder = new System.Text.StringBuilder();
            var toolCalls = new List<ContentBlock>();
            bool isDone = false;

            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                JsonElement json;
                try
                {
                    json = JsonSerializer.Deserialize<JsonElement>(line);
                }
                catch
                {
                    continue;
                }

                if (json.TryGetProperty("message", out var message))
                {
                    // Capture text content
                    if (message.TryGetProperty("content", out var content))
                    {
                        var text = content.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            textBuilder.Append(text);
                            yield return text;
                        }
                    }

                    // Capture tool calls (usually in final message)
                    if (message.TryGetProperty("tool_calls", out var toolCallsElement) &&
                        toolCallsElement.ValueKind == JsonValueKind.Array &&
                        toolCallsElement.GetArrayLength() > 0)
                    {
                        foreach (var toolCall in toolCallsElement.EnumerateArray())
                        {
                            if (toolCall.TryGetProperty("function", out var function))
                            {
                                var argumentsJson = function.TryGetProperty("arguments", out var args)
                                    ? args.GetString()
                                    : null;
                                var inputArgs = argumentsJson is not null
                                    ? JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson)
                                    : new Dictionary<string, object>();

                                toolCalls.Add(new ContentBlock
                                {
                                    Type = "tool_use",
                                    Id = Guid.NewGuid().ToString(),
                                    Name = function.TryGetProperty("name", out var name) ? name.GetString() : null,
                                    Input = inputArgs
                                });
                            }
                        }
                    }
                }

                if (json.TryGetProperty("done", out var done) && done.GetBoolean())
                {
                    isDone = true;
                    break;
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
            string stopReason = toolCalls.Count > 0 ? "tool_use" : "end_turn";

            // Populate streaming response
            streamingResponse.FinalResponse = new LlmResponse
            {
                StopReason = stopReason,
                Content = finalContent
            };
            streamingResponse.StopReason = stopReason;
            streamingResponse.AccumulatedText = accumulatedText;
            streamingResponse.IsComplete = isDone;
        }
    }
}
