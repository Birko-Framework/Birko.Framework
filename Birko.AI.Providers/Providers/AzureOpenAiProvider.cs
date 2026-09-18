using Birko.AI.Models;
using Birko.AI.Tools;
using System.Text;
using System.Text.Json;

namespace Birko.AI.Providers
{
    public class AzureOpenAiProvider : LlmProviderBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _deployment;

        public override string Name => "Azure OpenAI";

        public AzureOpenAiProvider(string endpoint, string apiKey, string deployment = "gpt-4")
        {
            _endpoint = endpoint.TrimEnd('/');
            _apiKey = apiKey;
            _deployment = deployment;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }

        public override async Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured()) return NotConfigured();

            try
            {
                var payload = new
                {
                    messages = BuildOpenAiStyleMessages(messages, systemPrompt),
                    tools = BuildOpenAiStyleTools(tools),
                    max_tokens = 16384  // GPT-4 max output tokens
                };
                var json = JsonSerializer.Serialize(payload);
                var url = $"{_endpoint}/openai/deployments/{_deployment}/chat/completions?api-version=2024-02-15-preview";

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
                    return LlmResponse.Error("Azure OpenAI: No response received");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorDetail = ExtractErrorFromResponseBody(responseJson) ?? responseJson;
                    var errorMsg = $"Azure OpenAI API Error ({response.StatusCode}): {errorDetail}";
                    SendMessage("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

                return ParseOpenAiStyleResponse(responseJson, MessageCallback);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error calling Azure OpenAI API: {ex.Message}";
                SendMessage("error", errorMsg);
                return LlmResponse.Error(errorMsg);
            }
        }

        protected override bool IsConfigured() => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_deployment);

        public override async Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured())
            {
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                        new InvalidOperationException("Azure OpenAI provider is not configured")),
                    Error = "Not configured"
                };
            }

            try
            {
                var payload = new
                {
                    messages = BuildOpenAiStyleMessages(messages, systemPrompt),
                    tools = BuildOpenAiStyleTools(tools),
                    max_tokens = 16384,
                    stream = true
                };
                var json = JsonSerializer.Serialize(payload);
                var url = $"{_endpoint}/openai/deployments/{_deployment}/chat/completions?api-version=2024-02-15-preview";

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
                            new HttpRequestException("Failed to connect to Azure OpenAI API after retries")),
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
                SendMessage("error", $"Error calling Azure OpenAI streaming API: {ex.Message}");
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(ex),
                    Error = ex.Message
                };
            }
        }
    }
}
