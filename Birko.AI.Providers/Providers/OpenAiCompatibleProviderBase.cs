using Birko.AI.Models;
using Birko.AI.Tools;
using System.Text;
using System.Text.Json;

namespace Birko.AI.Providers
{
    /// <summary>
    /// Base class for providers that implement the OpenAI-compatible API format.
    /// Used by local inference servers like llama.cpp, vLLM, SGLang, etc.
    /// </summary>
    public abstract class OpenAiCompatibleProviderBase : LlmProviderBase
    {
        protected readonly HttpClient HttpClient;
        protected readonly string Model;
        protected readonly string BaseUrl;

        /// <summary>
        /// Display name for this provider (e.g., "llama.cpp", "vLLM", "SGLang")
        /// </summary>
        protected abstract string ProviderName { get; }

        /// <summary>
        /// Max tokens to request. Override in derived class if needed.
        /// Default is 8192. Use -1 for unlimited (llama.cpp style).
        /// </summary>
        protected virtual int MaxTokens => 8192;

        /// <summary>
        /// Temperature for generation. Override in derived class if needed.
        /// </summary>
        protected virtual double Temperature => 0.7;

        /// <summary>
        /// HTTP timeout for requests. Local models can be slow.
        /// </summary>
        protected virtual TimeSpan RequestTimeout => TimeSpan.FromMinutes(5);

        public override string Name => $"{ProviderName} ({Model})";

        protected OpenAiCompatibleProviderBase(string model, string baseUrl, string? apiKey = null, HttpMessageHandler? handler = null)
        {
            Model = model;
            BaseUrl = baseUrl.TrimEnd('/');
            // handler is a test seam (inject a fake HttpMessageHandler); production callers omit it.
            HttpClient = new HttpClient(handler ?? new HttpClientHandler())
            {
                Timeout = RequestTimeout
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                HttpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        /// <summary>
        /// The chat-completions endpoint URL. Robust to the two BaseUrl conventions in use:
        /// derived providers that pass a bare host (Vllm/Sglang) get /v1/chat/completions
        /// appended, while providers that already pass the full endpoint path
        /// (Mistral/DeepSeek/Groq/OpenRouter/LmStudio) are used as-is — avoiding the doubled
        /// ".../chat/completions/v1/chat/completions" path that produced a 404 (CR-H004).
        /// </summary>
        protected string ChatCompletionsUrl =>
            BaseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? BaseUrl
                : $"{BaseUrl}/v1/chat/completions";

        public override async Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured()) return NotConfigured();

            try
            {
                var payload = BuildPayload(messages, tools, systemPrompt);
                var json = JsonSerializer.Serialize(payload);
                var url = ChatCompletionsUrl;

                // Use retry logic for transient failures
                var (response, responseJson) = await SendWithRetryAsync(
                    HttpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, url);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        return request;
                    },
                    ProviderName,
                    cancellationToken);

                if (response == null || responseJson == null)
                {
                    return LlmResponse.Error($"{ProviderName}: No response received");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorDetail = ExtractErrorFromResponseBody(responseJson) ?? responseJson;
                    var errorMsg = $"{ProviderName} API Error ({response.StatusCode}): {errorDetail}";
                    SendMessage("error", errorMsg);
                    return LlmResponse.Error(errorMsg, ClassifyResponse(response, responseJson));
                }

                return ParseResponse(responseJson);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                var errorMsg = $"Error calling {ProviderName} API: {ex.Message}";
                SendMessage("error", errorMsg);
                return LlmResponse.Error(errorMsg, ClassifyException(ex));
            }
        }

        /// <summary>
        /// The <c>messages</c> array of both payloads. Override where a provider's turn-order contract is
        /// stricter than OpenAI's (see <see cref="MistralProvider"/>).
        /// </summary>
        protected virtual List<object> BuildMessages(List<Message> messages, string systemPrompt)
            => BuildOpenAiStyleMessages(messages, systemPrompt);

        /// <summary>
        /// Build the request payload. Override to customize payload structure.
        /// </summary>
        protected virtual object BuildPayload(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            return new
            {
                model = Model,
                messages = BuildMessages(messages, systemPrompt),
                stream = false,
                tools = BuildOpenAiStyleTools(tools),
                temperature = Temperature,
                max_tokens = MaxTokens
            };
        }

        protected override bool IsConfigured() => !string.IsNullOrWhiteSpace(BaseUrl);

        /// <summary>
        /// Parse the OpenAI-compatible response through the one OpenAI-style parser (TASK-716): this copy read no
        /// <c>finish_reason</c> (a call cut off by the length limit was run) and no <c>usage</c>, and failed on
        /// <c>"error": null</c> (all measured). A missing tool-call id is still filled in.
        /// </summary>
        protected virtual LlmResponse ParseResponse(string responseJson)
            => ParseOpenAiStyleResponse(responseJson, MessageCallback, ProviderName);

        public override async Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured())
            {
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                        new InvalidOperationException($"{ProviderName} provider is not configured")),
                    Error = "Not configured"
                };
            }

            try
            {
                var payload = BuildStreamingPayload(messages, tools, systemPrompt);
                var json = JsonSerializer.Serialize(payload);
                var url = ChatCompletionsUrl;

                var (response, failure) = await ConnectStreamingAsync(
                    HttpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, url);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        return request;
                    },
                    ProviderName,
                    cancellationToken);

                if (response == null)
                {
                    return StreamingFailure($"Failed to connect to {ProviderName} API after retries", failure ?? LlmFailure.Unknown);
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
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                SendMessage("error", $"Error calling {ProviderName} streaming API: {ex.Message}");
                return StreamingFailure(ex.Message, ClassifyException(ex));
            }
        }

        /// <summary>
        /// Build streaming payload. Override to customize.
        /// </summary>
        protected virtual object BuildStreamingPayload(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            return new
            {
                model = Model,
                messages = BuildMessages(messages, systemPrompt),
                stream = true,
                tools = BuildOpenAiStyleTools(tools),
                temperature = Temperature,
                max_tokens = MaxTokens
            };
        }
    }
}
