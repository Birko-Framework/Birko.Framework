using Birko.AI.Models;
using Birko.AI.Tools;
using System.Net;
using System.Text.Json;

namespace Birko.AI.Providers
{
    public abstract class LlmProviderBase : ILlmProvider
    {
        public abstract string Name { get; }
        public Action<string, string>? MessageCallback { get; set; }

        /// <summary>
        /// Retry policy for transient failures. Can be overridden by derived classes.
        /// </summary>
        protected virtual RetryPolicy RetryPolicy { get; set; } = new()
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            AddJitter = true
        };

        public abstract Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt);

        public virtual Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            return Task.FromResult(new LlmStreamingResponse
            {
                GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(
                    new NotSupportedException($"Streaming is not supported by {Name} provider")),
                Error = "Streaming not supported"
            });
        }

        protected void SendMessage(string type, string message)
        {
            MessageCallback?.Invoke(type, message);
        }

        protected abstract bool IsConfigured();

        /// <summary>
        /// Sends an HTTP request with retry logic for transient failures.
        /// </summary>
        protected async Task<(HttpResponseMessage? Response, string? ResponseBody)> SendWithRetryAsync(
            HttpClient httpClient,
            Func<HttpRequestMessage> requestFactory,
            string providerName)
        {
            var policy = RetryPolicy;
            var attempt = 0;
            Exception? lastException = null;
            HttpResponseMessage? lastResponse = null;
            string? lastResponseBody = null;

            while (attempt <= policy.MaxRetries)
            {
                try
                {
                    using var request = requestFactory();
                    var response = await httpClient.SendAsync(request);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                        return (response, responseBody);

                    lastResponse = response;
                    lastResponseBody = responseBody;

                    if (!IsRetryableStatusCode(response.StatusCode))
                    {
                        var errorDetail = ExtractErrorFromResponseBody(responseBody);
                        var errorMsg = errorDetail != null
                            ? $"{providerName} API Error ({response.StatusCode}): {errorDetail}"
                            : $"{providerName} API Error: {response.StatusCode}";
                        SendMessage("error", errorMsg);
                        return (response, responseBody);
                    }

                    if (attempt >= policy.MaxRetries)
                    {
                        var errorDetail = ExtractErrorFromResponseBody(responseBody);
                        var errorMsg = errorDetail != null
                            ? $"{providerName} API Error after {attempt + 1} attempts ({response.StatusCode}): {errorDetail}"
                            : $"{providerName} API Error after {attempt + 1} attempts: {response.StatusCode}";
                        SendMessage("error", errorMsg);
                        return (response, responseBody);
                    }

                    var retryAfter = GetRetryAfterDelay(response);
                    var delay = retryAfter ?? policy.GetDelay(attempt + 1);

                    SendMessage("warning", $"{providerName}: {response.StatusCode}, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{policy.MaxRetries + 1})");

                    await Task.Delay(delay);
                    attempt++;
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    if (attempt >= policy.MaxRetries)
                    {
                        SendMessage("error", $"{providerName}: Network error after {attempt + 1} attempts: {ex.Message}");
                        throw;
                    }

                    var delay = policy.GetDelay(attempt + 1);
                    SendMessage("warning", $"{providerName}: Network error, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{policy.MaxRetries + 1}): {ex.Message}");

                    await Task.Delay(delay);
                    attempt++;
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    lastException = ex;
                    if (attempt >= policy.MaxRetries)
                    {
                        SendMessage("error", $"{providerName}: Timeout after {attempt + 1} attempts");
                        throw;
                    }

                    var delay = policy.GetDelay(attempt + 1);
                    SendMessage("warning", $"{providerName}: Timeout, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{policy.MaxRetries + 1})");

                    await Task.Delay(delay);
                    attempt++;
                }
            }

            if (lastException != null)
                throw lastException;

            return (lastResponse, lastResponseBody);
        }

        private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        {
            return statusCode switch
            {
                HttpStatusCode.TooManyRequests => true,
                HttpStatusCode.InternalServerError => true,
                HttpStatusCode.BadGateway => true,
                HttpStatusCode.ServiceUnavailable => true,
                HttpStatusCode.GatewayTimeout => true,
                HttpStatusCode.RequestTimeout => true,
                _ => (int)statusCode >= 500
            };
        }

        private static TimeSpan? GetRetryAfterDelay(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                var retryAfter = values.FirstOrDefault();
                if (retryAfter != null)
                {
                    if (int.TryParse(retryAfter, out var seconds))
                        return TimeSpan.FromSeconds(seconds);

                    if (DateTimeOffset.TryParse(retryAfter, out var date))
                    {
                        var delay = date - DateTimeOffset.UtcNow;
                        if (delay > TimeSpan.Zero)
                            return delay;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Attempts to extract a human-readable error message from an API response body.
        /// </summary>
        protected static string? ExtractErrorFromResponseBody(string? responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody)) return null;

            try
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(responseBody);

                if (doc.TryGetProperty("error", out var errorProp))
                {
                    if (errorProp.ValueKind == JsonValueKind.Object)
                    {
                        if (errorProp.TryGetProperty("message", out var msg))
                            return msg.GetString();
                        if (errorProp.TryGetProperty("msg", out var msg2))
                            return msg2.GetString();
                    }
                    else if (errorProp.ValueKind == JsonValueKind.String)
                    {
                        return errorProp.GetString();
                    }
                }

                if (doc.TryGetProperty("message", out var topMsg))
                    return topMsg.GetString();

                if (doc.TryGetProperty("detail", out var detail))
                {
                    if (detail.ValueKind == JsonValueKind.String)
                        return detail.GetString();
                }
            }
            catch
            {
                return responseBody.Length > 200 ? responseBody[..200] + "..." : responseBody;
            }

            return null;
        }

        protected static List<object> BuildOpenAiStyleMessages(IEnumerable<Message> messages, string systemPrompt)
        {
            var list = new List<object> { new { role = "system", content = systemPrompt } };
            foreach (var m in messages)
            {
                object? content = m.Content ?? "";

                if (m.Content is IEnumerable<ContentBlock> blocks)
                {
                    var blocksList = blocks.ToList();
                    var textBlocks = blocksList.Where(b => b.Type?.ToLowerInvariant() == "text").ToList();
                    var toolUseBlocks = blocksList.Where(b => b.Type?.ToLowerInvariant() == "tool_use").ToList();

                    if (m.Role == "assistant" && toolUseBlocks.Any())
                    {
                        var textContent = textBlocks.Any() && !string.IsNullOrEmpty(textBlocks[0].Text)
                            ? textBlocks[0].Text
                            : null;

                        var toolCalls = toolUseBlocks.Select(b => new
                        {
                            id = b.Id,
                            type = "function",
                            function = new
                            {
                                name = b.Name,
                                arguments = JsonSerializer.Serialize(b.Input ?? new Dictionary<string, object>())
                            }
                        }).ToList();

                        list.Add(new { role = m.Role, content = textContent, tool_calls = toolCalls });
                        continue;
                    }

                    if (textBlocks.Any())
                    {
                        content = textBlocks.Count == 1 ? textBlocks[0].Text :
                            string.Join("\n", textBlocks.Select(b => b.Text));
                    }
                    else
                    {
                        content = "";
                    }
                }
                else if (m.Content is IEnumerable<object> objs && objs.Any())
                {
                    var objsList = objs.ToList();
                    var firstObj = objsList.First();
                    var firstObjType = firstObj.GetType();

                    if (firstObjType.GetProperty("type") != null)
                    {
                        foreach (var obj in objsList)
                        {
                            var objType = obj.GetType();
                            var toolCallIdProp = objType.GetProperty("tool_use_id");
                            var contentProp = objType.GetProperty("content");

                            if (toolCallIdProp != null && contentProp != null)
                            {
                                var toolCallId = toolCallIdProp.GetValue(obj)?.ToString();
                                var toolContent = contentProp.GetValue(obj)?.ToString() ?? "";

                                list.Add(new { role = "tool", tool_call_id = toolCallId, content = toolContent });
                            }
                        }
                        continue;
                    }
                }
                else if (m.Content is JsonElement jsonContent)
                {
                    if (ConvertJsonElementMessage(list, m.Role ?? "user", jsonContent))
                        continue;
                    content = jsonContent.ValueKind == JsonValueKind.String
                        ? jsonContent.GetString() ?? ""
                        : (object)jsonContent.GetRawText();
                }

                list.Add(new { role = m.Role, content });
            }
            return list;
        }

        private static bool ConvertJsonElementMessage(List<object> list, string role, JsonElement jsonContent)
        {
            if (jsonContent.ValueKind != JsonValueKind.Array)
                return false;

            var textParts = new List<string>();
            var toolCalls = new List<object>();
            var toolResults = new List<(string? toolCallId, string content)>();

            foreach (var element in jsonContent.EnumerateArray())
            {
                if (!element.TryGetProperty("type", out var typeProp))
                    continue;

                var type = typeProp.GetString()?.ToLowerInvariant();
                switch (type)
                {
                    case "text":
                        if (element.TryGetProperty("text", out var textProp))
                            textParts.Add(textProp.GetString() ?? "");
                        break;

                    case "tool_use":
                        var id = element.TryGetProperty("id", out var idProp)
                            ? idProp.GetString() ?? $"call_{Guid.NewGuid():N}"
                            : $"call_{Guid.NewGuid():N}";
                        var name = element.TryGetProperty("name", out var nameProp)
                            ? nameProp.GetString() ?? ""
                            : "";
                        var arguments = element.TryGetProperty("input", out var inputProp)
                            ? inputProp.GetRawText()
                            : "{}";
                        toolCalls.Add(new
                        {
                            id,
                            type = "function",
                            function = new { name, arguments }
                        });
                        break;

                    case "tool_result":
                        var toolCallId = element.TryGetProperty("tool_use_id", out var toolIdProp)
                            ? toolIdProp.GetString()
                            : null;
                        var resultContent = "";
                        if (element.TryGetProperty("content", out var contentProp))
                        {
                            resultContent = contentProp.ValueKind == JsonValueKind.String
                                ? contentProp.GetString() ?? ""
                                : contentProp.GetRawText();
                        }
                        toolResults.Add((toolCallId, resultContent));
                        break;
                }
            }

            if (toolResults.Count > 0)
            {
                foreach (var (toolCallId, resultContent) in toolResults)
                    list.Add(new { role = "tool", tool_call_id = toolCallId, content = resultContent });
                return true;
            }

            if (toolCalls.Count > 0 && role == "assistant")
            {
                var textContent = textParts.Count > 0 ? string.Join("\n", textParts) : (string?)null;
                list.Add(new { role, content = textContent, tool_calls = toolCalls });
                return true;
            }

            if (textParts.Count > 0)
            {
                list.Add(new { role, content = string.Join("\n", textParts) });
                return true;
            }

            return false;
        }

        protected static object BuildOpenAiStyleTools(IEnumerable<Tool> tools) => tools.Select(t => new
        {
            type = "function",
            function = new { name = t.Name, description = t.Description, parameters = t.InputSchema }
        }).ToList();

        protected static LlmResponse ParseOpenAiStyleResponse(string responseJson, Action<string, string>? messageCallback = null)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

                if (result.TryGetProperty("error", out var error))
                {
                    var errorMessage = error.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
                    var errorType = error.TryGetProperty("type", out var type) ? type.GetString() : "unknown";
                    var errorMsg = $"API returned error: {errorType} - {errorMessage}";
                    messageCallback?.Invoke("error", errorMsg);
                    return LlmResponse.Error(errorMsg);
                }

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
                        llmResponse.Content.Add(new ContentBlock { Type = "text", Text = textContent.GetString() });
                }

                if (result.TryGetProperty("usage", out var usage))
                {
                    llmResponse.Usage = new TokenUsage
                    {
                        PromptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                        CompletionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0,
                        Model = result.TryGetProperty("model", out var mdl) ? mdl.GetString() : null
                    };
                }

                return llmResponse;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error parsing OpenAI-style response: {ex.Message}";
                messageCallback?.Invoke("error", errorMsg);
                return LlmResponse.Error(errorMsg);
            }
        }

        protected static LlmResponse NotConfigured() => new() { StopReason = "NotConfigured", Content = [], ErrorMessage = "Provider is not properly configured. Check API keys and settings." };

        /// <summary>
        /// Sends a streaming HTTP request with retry logic for initial connection failures.
        /// </summary>
        protected async Task<HttpResponseMessage?> SendStreamingWithRetryAsync(
            HttpClient httpClient,
            Func<HttpRequestMessage> requestFactory,
            string providerName)
        {
            var policy = RetryPolicy;
            var attempt = 0;

            while (attempt <= policy.MaxRetries)
            {
                try
                {
                    var request = requestFactory();
                    var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                    if (response.IsSuccessStatusCode)
                        return response;

                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (!IsRetryableStatusCode(response.StatusCode))
                    {
                        SendMessage("error", $"{providerName} API Error: {response.StatusCode}");
                        response.Dispose();
                        return null;
                    }

                    if (attempt >= policy.MaxRetries)
                    {
                        SendMessage("error", $"{providerName} API Error after {attempt + 1} attempts: {response.StatusCode}");
                        response.Dispose();
                        return null;
                    }

                    var retryAfter = GetRetryAfterDelay(response);
                    var delay = retryAfter ?? policy.GetDelay(attempt + 1);

                    SendMessage("warning", $"{providerName}: {response.StatusCode}, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{policy.MaxRetries + 1})");

                    response.Dispose();
                    await Task.Delay(delay);
                    attempt++;
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= policy.MaxRetries)
                    {
                        SendMessage("error", $"{providerName}: Network error after {attempt + 1} attempts: {ex.Message}");
                        return null;
                    }

                    var delay = policy.GetDelay(attempt + 1);
                    SendMessage("warning", $"{providerName}: Network error, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{policy.MaxRetries + 1}): {ex.Message}");

                    await Task.Delay(delay);
                    attempt++;
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    if (attempt >= policy.MaxRetries)
                    {
                        SendMessage("error", $"{providerName}: Timeout after {attempt + 1} attempts");
                        return null;
                    }

                    var delay = policy.GetDelay(attempt + 1);
                    SendMessage("warning", $"{providerName}: Timeout, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{policy.MaxRetries + 1})");

                    await Task.Delay(delay);
                    attempt++;
                }
            }

            return null;
        }

        /// <summary>
        /// Parses SSE (Server-Sent Events) stream format.
        /// </summary>
        protected static async IAsyncEnumerable<string> ParseSseStream(Stream stream)
        {
            using var reader = new StreamReader(stream);
            var buffer = new System.Text.StringBuilder();

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrEmpty(line))
                {
                    if (buffer.Length > 0)
                    {
                        var data = buffer.ToString();
                        buffer.Clear();

                        if (data == "[DONE]")
                            yield break;

                        yield return data;
                    }
                    continue;
                }

                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);

                    if (data == "[DONE]")
                        yield break;

                    buffer.AppendLine(data);
                }
            }

            if (buffer.Length > 0)
                yield return buffer.ToString();
        }

        /// <summary>
        /// Parses OpenAI-style streaming response chunks.
        /// </summary>
        protected static async IAsyncEnumerable<string> ParseOpenAiStreamChunks(IAsyncEnumerable<string> sseChunks)
        {
            await foreach (var chunk in sseChunks)
            {
                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                JsonElement json;
                try { json = JsonSerializer.Deserialize<JsonElement>(chunk); }
                catch { continue; }

                if (!json.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;

                var delta = choices[0].GetProperty("delta");

                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    var text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                        yield return text;
                }
            }
        }

        /// <summary>
        /// Parses OpenAI-style streaming response chunks with tool call capture.
        /// </summary>
        protected static async IAsyncEnumerable<string> ParseOpenAiStreamChunksWithToolCapture(
            IAsyncEnumerable<string> sseChunks,
            LlmStreamingResponse streamingResponse)
        {
            var textBuilder = new System.Text.StringBuilder();
            var toolCalls = new Dictionary<int, (string? Id, string? Name, System.Text.StringBuilder Arguments)>();
            string? finishReason = null;
            TokenUsage? tokenUsage = null;

            await foreach (var chunk in sseChunks)
            {
                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                JsonElement json;
                try { json = JsonSerializer.Deserialize<JsonElement>(chunk); }
                catch { continue; }

                if (json.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
                {
                    tokenUsage = new TokenUsage
                    {
                        PromptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                        CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0,
                        Model = json.TryGetProperty("model", out var mdl) ? mdl.GetString() : null
                    };
                }

                if (!json.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];

                if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    finishReason = fr.GetString();

                if (!choice.TryGetProperty("delta", out var delta))
                    continue;

                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    var text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        textBuilder.Append(text);
                        yield return text;
                    }
                }

                if (delta.TryGetProperty("tool_calls", out var toolCallsDelta))
                {
                    foreach (var toolCallDelta in toolCallsDelta.EnumerateArray())
                    {
                        var index = toolCallDelta.GetProperty("index").GetInt32();

                        if (!toolCalls.ContainsKey(index))
                            toolCalls[index] = (null, null, new System.Text.StringBuilder());

                        var entry = toolCalls[index];

                        if (toolCallDelta.TryGetProperty("id", out var id))
                            entry.Id = id.GetString();

                        if (toolCallDelta.TryGetProperty("function", out var function))
                        {
                            if (function.TryGetProperty("name", out var name))
                                entry.Name = name.GetString();
                            if (function.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String)
                                entry.Arguments.Append(args.GetString());
                        }

                        toolCalls[index] = entry;
                    }
                }
            }

            var finalContent = new List<ContentBlock>();

            var accumulatedText = textBuilder.ToString();
            if (!string.IsNullOrEmpty(accumulatedText))
                finalContent.Add(new ContentBlock { Type = "text", Text = accumulatedText });

            foreach (var (index, (id, name, arguments)) in toolCalls.OrderBy(kv => kv.Key))
            {
                var argsJson = arguments.ToString();
                Dictionary<string, object>? inputDict = null;

                if (!string.IsNullOrEmpty(argsJson))
                {
                    try { inputDict = JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson); }
                    catch { inputDict = new Dictionary<string, object> { ["_raw"] = argsJson }; }
                }

                finalContent.Add(new ContentBlock
                {
                    Type = "tool_use",
                    Id = id,
                    Name = name,
                    Input = inputDict ?? new Dictionary<string, object>()
                });
            }

            string stopReason;
            if (toolCalls.Count > 0)
                stopReason = "tool_use";
            else if (finishReason == "stop" || finishReason == null)
                stopReason = "end_turn";
            else
                stopReason = finishReason;

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
