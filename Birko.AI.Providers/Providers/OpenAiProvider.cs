using Birko.AI.Models;
using Birko.AI.Tools;
using System.Text;
using System.Text.Json;

namespace Birko.AI.Providers
{
    public class OpenAiProvider : LlmProviderBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string? _baseUrl;

        public override string Name => "OpenAI";

        public OpenAiProvider(string apiKey, string model = "gpt-4o", string baseUrl = "https://api.openai.com/v1/chat/completions")
        {
            _apiKey = apiKey;
            _model = model;
            _baseUrl = baseUrl;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
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
                    tools = BuildOpenAiStyleTools(tools),
                    max_tokens = 16384  // GPT-4o/4o-mini max output tokens
                };
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
                    Name,
                    cancellationToken);

                if (response == null || responseJson == null)
                {
                    return LlmResponse.Error("OpenAI: No response received");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorDetail = ExtractErrorFromResponseBody(responseJson) ?? responseJson;
                    var errorMsg = $"OpenAI API Error ({response.StatusCode}): {errorDetail}";
                    SendMessage("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

                return ParseOpenAiStyleResponse(responseJson, MessageCallback);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error calling OpenAI API: {ex.Message}";
                SendMessage("error", errorMsg);
                return LlmResponse.Error(errorMsg);
            }
        }

        protected override bool IsConfigured()
            => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_model);

        private static List<object> BuildMessages(IEnumerable<Message> messages, string systemPrompt)
        {
            var openAiMessages = new List<object> { new { role = "system", content = systemPrompt } };
            openAiMessages.AddRange(messages.Select(m => new { role = m.Role, content = m.Content }));
            return openAiMessages;
        }

        private static object BuildRequestPayload(IEnumerable<Message> messages, IEnumerable<Tool> tools, string systemPrompt) => new
        {
            model = "" /* replaced below */,
            messages = BuildMessages(messages, systemPrompt),
            tools = tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.InputSchema }
            }).ToList()
        };

        private static LlmResponse ParseResponse(string responseJson)
        {
            var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
            var choice = result.GetProperty("choices")[0];
            var message = choice.GetProperty("message");

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
                        Id = toolCall.GetProperty("id").GetString(),
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
                        new InvalidOperationException("OpenAI provider is not configured")),
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
                    max_tokens = 16384,
                    stream = true
                };
                var json = JsonSerializer.Serialize(payload);

                // Use retry logic for initial connection
                var response = await SendStreamingWithRetryAsync(
                    _httpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
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
                            new HttpRequestException("Failed to connect to OpenAI API after retries")),
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
                SendMessage("error", $"Error calling OpenAI streaming API: {ex.Message}");
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(ex),
                    Error = ex.Message
                };
            }
        }
    }
}
