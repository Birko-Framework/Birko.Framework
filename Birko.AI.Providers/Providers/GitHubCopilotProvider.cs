using Birko.AI.Models;
using Birko.AI.Tools;
using Birko.Communication.OAuth;
using System.Text;
using System.Text.Json;

namespace Birko.AI.Providers
{
    public class GitHubCopilotProvider : LlmProviderBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _model;
        private readonly string _baseUrl;
        private readonly IOAuthClient _oauthClient;
        private OAuthToken? _currentToken;

        public override string Name => "GitHub Copilot";

        /// <summary>
        /// Creates a GitHub Copilot provider using a pre-configured IOAuthClient.
        /// Use GitHubOAuthProvider.CreateDeviceFlowClient() from Birko.Communication.OAuth.Providers.
        /// </summary>
        public GitHubCopilotProvider(
            IOAuthClient oauthClient,
            string model = "gpt-4o",
            string baseUrl = "https://api.githubcopilot.com/chat/completions")
        {
            _oauthClient = oauthClient ?? throw new ArgumentNullException(nameof(oauthClient));
            _model = model;
            _baseUrl = baseUrl;
            _httpClient = new HttpClient();
        }

        public override async Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            if (!await EnsureAuthenticatedAsync())
                return NotConfigured();

            var payload = new
            {
                model = _model,
                messages = BuildOpenAiStyleMessages(messages, systemPrompt),
                tools = BuildOpenAiStyleTools(tools),
                max_tokens = 16384
            };

            var json = JsonSerializer.Serialize(payload);

            HttpRequestMessage CreateRequest()
            {
                var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
                request.Headers.Add("Authorization", $"Bearer {_currentToken!.AccessToken}");
                request.Headers.Add("Editor-Version", "vscode/1.96.0");
                request.Headers.Add("Editor-Plugin-Version", "copilot-chat/0.23.2");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return request;
            }

            try
            {
                var (response, responseJson) = await SendWithRetryAsync(
                    _httpClient,
                    CreateRequest,
                    Name);

                if (response == null || responseJson == null)
                    return LlmResponse.Error("GitHub Copilot: No response received");

                // Handle 401 — refresh token and retry once
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _currentToken = await _oauthClient.RefreshTokenAsync();
                    (response, responseJson) = await SendWithRetryAsync(
                        _httpClient,
                        CreateRequest,
                        Name);
                }

                if (response == null || responseJson == null || !response.IsSuccessStatusCode)
                {
                    var errorDetail = responseJson != null ? (ExtractErrorFromResponseBody(responseJson) ?? responseJson) : "No response";
                    var errorMsg = $"GitHub Copilot API Error ({response?.StatusCode}): {errorDetail}";
                    SendMessage("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

                return ParseOpenAiStyleResponse(responseJson, MessageCallback);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error calling GitHub Copilot API: {ex.Message}";
                SendMessage("error", errorMsg);
                return LlmResponse.Error(errorMsg);
            }
        }

        private async Task<bool> EnsureAuthenticatedAsync()
        {
            if (_currentToken == null || _currentToken.IsExpiredWithBuffer(60))
                _currentToken = await _oauthClient.GetTokenAsync();

            return _currentToken != null;
        }

        protected override bool IsConfigured() => !string.IsNullOrWhiteSpace(_model);

        public override async Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            if (!IsConfigured())
            {
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                        new InvalidOperationException("GitHub Copilot provider is not configured")),
                    Error = "Not configured"
                };
            }

            if (!await EnsureAuthenticatedAsync())
            {
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                        new InvalidOperationException("Failed to authenticate with GitHub Copilot")),
                    Error = "Authentication failed"
                };
            }

            try
            {
                var payload = new
                {
                    model = _model,
                    messages = BuildOpenAiStyleMessages(messages, systemPrompt),
                    tools = BuildOpenAiStyleTools(tools),
                    max_tokens = 4096,
                    stream = true
                };
                var json = JsonSerializer.Serialize(payload);

                var response = await SendStreamingWithRetryAsync(
                    _httpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _currentToken!.AccessToken);
                        return request;
                    },
                    Name);

                if (response == null)
                {
                    return new LlmStreamingResponse
                    {
                        GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                            new HttpRequestException("Failed to connect to GitHub Copilot API after retries")),
                        Error = "Connection failed"
                    };
                }

                var streamingResponse = new LlmStreamingResponse
                {
                    IsComplete = false,
                    GetStreamAsync = null!
                };

                streamingResponse.GetStreamAsync = async () =>
                {
                    var stream = await response.Content.ReadAsStreamAsync();
                    var sseStream = ParseSseStream(stream);
                    return ParseOpenAiStreamChunksWithToolCapture(sseStream, streamingResponse);
                };

                return streamingResponse;
            }
            catch (Exception ex)
            {
                SendMessage("error", $"Error calling GitHub Copilot streaming API: {ex.Message}");
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(ex),
                    Error = ex.Message
                };
            }
        }
    }
}
