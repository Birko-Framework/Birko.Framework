using Birko.AI.Models;
using Birko.AI.Tools;
using System.Net;
using System.Runtime.CompilerServices;
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

        public abstract Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default);

        public virtual Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
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
            string providerName,
            CancellationToken cancellationToken = default)
        {
            var policy = RetryPolicy;
            // SH-L148: a negative MaxRetries skipped the loop and returned (null, null) with nothing sent.
            var maxRetries = Math.Max(0, policy.MaxRetries);
            var attempt = 0;

            while (attempt <= maxRetries)
            {
                try
                {
                    using var request = requestFactory();
                    // The body is fully buffered into responseBody below, so callers never need the
                    // live response (they only read StatusCode/IsSuccessStatusCode, which remain
                    // readable after Dispose). Dispose on every path to avoid leaking the response
                    // handle (CR-M010).
                    var response = await httpClient.SendAsync(request, cancellationToken);
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        response.Dispose();
                        return (response, responseBody);
                    }

                    if (!IsRetryableStatusCode(response.StatusCode))
                    {
                        var errorDetail = ExtractErrorFromResponseBody(responseBody);
                        var errorMsg = errorDetail != null
                            ? $"{providerName} API Error ({response.StatusCode}): {errorDetail}"
                            : $"{providerName} API Error: {response.StatusCode}";
                        SendMessage("error", errorMsg);
                        response.Dispose();
                        return (response, responseBody);
                    }

                    if (attempt >= maxRetries)
                    {
                        var errorDetail = ExtractErrorFromResponseBody(responseBody);
                        var errorMsg = errorDetail != null
                            ? $"{providerName} API Error after {attempt + 1} attempts ({response.StatusCode}): {errorDetail}"
                            : $"{providerName} API Error after {attempt + 1} attempts: {response.StatusCode}";
                        SendMessage("error", errorMsg);
                        response.Dispose();
                        return (response, responseBody);
                    }

                    // Read Retry-After (needs the live headers) before disposing.
                    var retryAfter = GetRetryAfterDelay(response);
                    var delay = retryAfter ?? policy.GetDelay(attempt + 1);

                    SendMessage("warning", $"{providerName}: {response.StatusCode}, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{maxRetries + 1})");

                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    attempt++;
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= maxRetries)
                    {
                        SendMessage("error", $"{providerName}: Network error after {attempt + 1} attempts: {ex.Message}");
                        throw;
                    }

                    var delay = policy.GetDelay(attempt + 1);
                    SendMessage("warning", $"{providerName}: Network error, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{maxRetries + 1}): {ex.Message}");

                    await Task.Delay(delay, cancellationToken);
                    attempt++;
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    if (attempt >= maxRetries)
                    {
                        SendMessage("error", $"{providerName}: Timeout after {attempt + 1} attempts");
                        throw;
                    }

                    var delay = policy.GetDelay(attempt + 1);
                    SendMessage("warning", $"{providerName}: Timeout, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{maxRetries + 1})");

                    await Task.Delay(delay, cancellationToken);
                    attempt++;
                }
            }

            // SH-L147: every last attempt returns or rethrows in place (`throw;`), so the loop cannot fall through.
            throw new InvalidOperationException("SendWithRetryAsync ended without a result.");
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
            var position = -1;
            foreach (var m in messages)
            {
                position++;
                // SH-L154: a turn with no role went out as "role": null and the API refused the whole request, naming no turn.
                if (string.IsNullOrEmpty(m.Role))
                    throw new InvalidOperationException($"Message {position} has no role.");
                object? content = m.Content ?? "";

                if (m.Content is IEnumerable<ContentBlock> blocks)
                {
                    var blocksList = blocks.ToList();
                    // SH-L153: matched exactly, as Agent dispatches them; a "Tool_Use" block was sent as a call the agent never ran.
                    var textBlocks = blocksList.Where(b => b.Type == "text").ToList();
                    var toolUseBlocks = blocksList.Where(b => b.Type == "tool_use").ToList();

                    if (m.Role == "assistant" && toolUseBlocks.Any())
                    {
                        // SH-M165: every text block, joined as the non-tool branch joins them; only the first was kept.
                        var joined = string.Join("\n", textBlocks.Select(b => b.Text).Where(t => !string.IsNullOrEmpty(t)));
                        var textContent = joined.Length > 0 ? joined : null;

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

                    if (ReadMember(firstObj, "type") != null)
                    {
                        foreach (var obj in objsList)
                        {
                            // SH-M166: read from an anonymous object, a dictionary or a JsonElement alike; an item that is not a
                            // tool result is refused (rule 41) — the whole message used to vanish, leaving its tool_use unanswered.
                            if (ReadMember(obj, "tool_use_id") is not { } toolCallId)
                                throw new NotSupportedException(
                                    $"A {m.Role} message item of type '{ReadMember(obj, "type")}' cannot be sent to an OpenAI-style API; only tool_result items are.");
                            list.Add(new { role = "tool", tool_call_id = toolCallId, content = ReadMember(obj, "content") ?? "" });
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

        // A named member of a content item: an anonymous object's property, a dictionary entry or a JsonElement property.
        private static string? ReadMember(object? item, string name) => item switch
        {
            null => null,
            IDictionary<string, object?> dictionary => dictionary.TryGetValue(name, out var v) ? v?.ToString() : null,
            JsonElement { ValueKind: JsonValueKind.Object } json => json.TryGetProperty(name, out var v)
                ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
                : null,
            JsonElement => null,
            _ => item.GetType().GetProperty(name)?.GetValue(item)?.ToString(),
        };

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

                // SH-M157: a gateway that always sends "error": null is not reporting an error.
                if (result.TryGetProperty("error", out var error) && error.ValueKind is JsonValueKind.Object or JsonValueKind.String)
                {
                    var errorMessage = error.ValueKind == JsonValueKind.String ? error.GetString() : ReadString(error, "message") ?? "Unknown error";
                    var errorType = error.ValueKind == JsonValueKind.Object ? ReadString(error, "type") ?? "unknown" : "unknown";
                    var errorMsg = $"API returned error: {errorType} - {errorMessage}";
                    messageCallback?.Invoke("error", errorMsg);
                    return LlmResponse.Error(errorMsg, new LlmFailure(LlmFailureKind.InvalidResponse));
                }

                if (!result.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                    throw new JsonException("The response has no choices.");
                var choice = choices[0];
                var message = choice.GetProperty("message");
                var finishReason = ReadString(choice, "finish_reason");

                var llmResponse = new LlmResponse { Content = [] };
                if (message.TryGetProperty("content", out var textContent) && textContent.ValueKind == JsonValueKind.String)
                    llmResponse.Content.Add(new ContentBlock { Type = "text", Text = textContent.GetString() });

                // SH-M158: "tool_calls": null on a text turn is no tool call.
                var hasToolCalls = message.TryGetProperty("tool_calls", out var toolCalls)
                    && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0;
                llmResponse.StopReason = MapFinishReason(finishReason, hasToolCalls);

                // SH-M160: a call cut off by the length limit has arguments that cannot be trusted, so it is not returned.
                if (hasToolCalls && llmResponse.StopReason == "tool_use")
                {
                    // A text block before tool calls is kept, as Claude's content keeps it.
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        var function = toolCall.GetProperty("function");
                        var argumentsJson = ReadString(function, "arguments");
                        var args = !string.IsNullOrEmpty(argumentsJson) ? JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson) : [];
                        llmResponse.Content.Add(new ContentBlock
                        {
                            Type = "tool_use",
                            Id = ReadString(toolCall, "id"),
                            Name = ReadString(function, "name"),
                            Input = args
                        });
                    }
                }

                if (result.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    llmResponse.Usage = new TokenUsage
                    {
                        PromptTokens = ReadTokenCount(usage, "prompt_tokens"),
                        CompletionTokens = ReadTokenCount(usage, "completion_tokens"),
                        Model = ReadString(result, "model")
                    };
                }

                return llmResponse;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error parsing OpenAI-style response: {ex.Message}";
                messageCallback?.Invoke("error", errorMsg);
                return LlmResponse.Error(errorMsg, new LlmFailure(LlmFailureKind.InvalidResponse));
            }
        }

        /// <summary>
        /// The stop reason for an OpenAI-style <c>finish_reason</c> (TASK-160, SH-M160 / M161), the one mapping the sync and
        /// streaming parsers share: <c>length</c> is <c>max_tokens</c> and <c>content_filter</c> stays itself, even when the
        /// response carries tool calls (a cut-off call is not run); otherwise tool calls make it <c>tool_use</c>; <c>stop</c>,
        /// none, or a tool-call reason with no call is <c>end_turn</c>.
        /// </summary>
        protected static string MapFinishReason(string? finishReason, bool hasToolCalls) => finishReason switch
        {
            "length" => "max_tokens",
            "content_filter" => "content_filter",
            _ when hasToolCalls => "tool_use",
            null or "stop" or "tool_calls" or "function_call" => "end_turn",
            _ => finishReason,
        };

        /// <summary>A token count as the API sent it: a whole number, a whole-valued fraction, or 0 for anything else (SH-M159 / M162).</summary>
        protected static int ReadTokenCount(JsonElement usage, string name)
        {
            if (!usage.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) return 0;
            if (value.TryGetInt32(out var whole)) return whole;
            return value.TryGetDouble(out var d) && d >= 0 && d <= int.MaxValue ? (int)Math.Round(d) : 0;
        }

        private static string? ReadString(JsonElement element, string name)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        protected static LlmResponse NotConfigured() => new()
        {
            StopReason = "NotConfigured",
            Content = [],
            ErrorMessage = "Provider is not properly configured. Check API keys and settings.",
            Failure = new LlmFailure(LlmFailureKind.NotConfigured),
        };

        // ── Failure classification (TASK-531) ──
        // One producer for "why did this call fail". Providers used to build LlmResponse.Error(string) on every path,
        // so the HTTP status this class already had was thrown away and consumers matched substrings of the message
        // (DraCode's ErrorClassifier: "500" and "timeout" matched unrelated text, quota exhaustion counted as transient).

        /// <summary>
        /// True when <paramref name="ex"/> is the caller cancelling: such a call has not failed and must not be answered
        /// with an error response. Every provider's catch-all is <c>catch (Exception ex) when (!IsCallerCancellation(ex, ct))</c>,
        /// so the cancellation propagates; before TASK-531 it came back as <c>StopReason = "error"</c>.
        /// </summary>
        protected static bool IsCallerCancellation(Exception ex, CancellationToken cancellationToken)
            => ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

        /// <summary>Classifies a non-success response returned by <see cref="SendWithRetryAsync"/> (the headers survive its disposal).</summary>
        protected static LlmFailure ClassifyResponse(HttpResponseMessage response, string? responseBody)
        {
            var status = (int)response.StatusCode;
            if (status == 429 && IsQuotaExhausted(responseBody))
                return new LlmFailure(LlmFailureKind.Billing, status);
            return LlmFailure.FromStatusCode(status, GetRetryAfterDelay(response));
        }

        /// <summary>Classifies an exception a provider caught (never the caller's cancellation, see <see cref="IsCallerCancellation"/>).</summary>
        protected static LlmFailure ClassifyException(Exception ex) => ex switch
        {
            HttpRequestException { StatusCode: { } code } => LlmFailure.FromStatusCode((int)code),
            HttpRequestException => new LlmFailure(LlmFailureKind.Network),
            TimeoutException => new LlmFailure(LlmFailureKind.Timeout),
            // HttpClient.Timeout surfaces as a TaskCanceledException the caller's token did not cause.
            OperationCanceledException => new LlmFailure(LlmFailureKind.Timeout),
            JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException
                => new LlmFailure(LlmFailureKind.InvalidResponse),
            _ => LlmFailure.Unknown,
        };

        /// <summary>
        /// OpenAI answers an exhausted quota with 429 and <c>error.code</c> / <c>error.type</c> <c>insufficient_quota</c>:
        /// a billing state, not a rate limit, so retrying it only spends time. Read from the structured code, not the text.
        /// </summary>
        private static bool IsQuotaExhausted(string? responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody)) return false;
            try
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(responseBody);
                if (doc.ValueKind != JsonValueKind.Object || !doc.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                    return false;
                foreach (var name in new[] { "code", "type" })
                {
                    if (error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                        && value.GetString() == "insufficient_quota")
                        return true;
                }
            }
            catch (JsonException)
            {
            }
            return false;
        }

        // SH-L146: a failed streaming request's message carries the provider's explanation, as the non-streaming one does.
        private static string ErrorMessage(string providerName, HttpStatusCode status, string? body, int? attempts)
        {
            var detail = ExtractErrorFromResponseBody(body);
            var after = attempts is { } n ? $" after {n} attempts" : "";
            return detail != null ? $"{providerName} API Error{after} ({status}): {detail}" : $"{providerName} API Error{after}: {status}";
        }

        /// <summary>A streaming response for a request that failed before any stream existed.</summary>
        protected static LlmStreamingResponse StreamingFailure(string message, LlmFailure failure) => new()
        {
            GetStreamAsync = () => Task.FromException<IAsyncEnumerable<string>>(new HttpRequestException(message)),
            Error = message,
            FinalResponse = LlmResponse.Error(message, failure),
        };

        /// <summary>The result of <see cref="ConnectStreamingAsync"/>: an open response, or why there is none.</summary>
        protected readonly record struct StreamingConnection(HttpResponseMessage? Response, LlmFailure? Failure);

        /// <summary>
        /// Sends a streaming HTTP request with retry logic for initial connection failures.
        /// </summary>
        /// <returns>The open response, or <c>null</c> when the request failed; <see cref="ConnectStreamingAsync"/> also says why.</returns>
        protected async Task<HttpResponseMessage?> SendStreamingWithRetryAsync(
            HttpClient httpClient,
            Func<HttpRequestMessage> requestFactory,
            string providerName,
            CancellationToken cancellationToken = default)
            => (await ConnectStreamingAsync(httpClient, requestFactory, providerName, cancellationToken)).Response;

        /// <summary>
        /// Opens a streaming HTTP request with retry logic for initial connection failures, and classifies the failure
        /// when there is no stream (TASK-531). The caller's cancellation propagates.
        /// </summary>
        protected async Task<StreamingConnection> ConnectStreamingAsync(
            HttpClient httpClient,
            Func<HttpRequestMessage> requestFactory,
            string providerName,
            CancellationToken cancellationToken = default)
        {
            var policy = RetryPolicy;
            var maxRetries = Math.Max(0, policy.MaxRetries);
            var attempt = 0;

            while (attempt <= maxRetries)
            {
                try
                {
                    // SH-M164: the request (and its serialized prompt) is disposed on every attempt; the response, read with
                    // ResponseHeadersRead, does not need it once the headers are in.
                    using var request = requestFactory();
                    var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (response.IsSuccessStatusCode)
                        return new StreamingConnection(response, null);

                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!IsRetryableStatusCode(response.StatusCode))
                    {
                        SendMessage("error", ErrorMessage(providerName, response.StatusCode, responseBody, null));
                        var failure = ClassifyResponse(response, responseBody);
                        response.Dispose();
                        return new StreamingConnection(null, failure);
                    }

                    if (attempt >= maxRetries)
                    {
                        SendMessage("error", ErrorMessage(providerName, response.StatusCode, responseBody, attempt + 1));
                        var failure = ClassifyResponse(response, responseBody);
                        response.Dispose();
                        return new StreamingConnection(null, failure);
                    }

                    var retryAfter = GetRetryAfterDelay(response);
                    var delay = retryAfter ?? policy.GetDelay(attempt + 1);

                    SendMessage("warning", $"{providerName}: {response.StatusCode}, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{maxRetries + 1})");

                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    attempt++;
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= maxRetries)
                    {
                        SendMessage("error", $"{providerName}: Network error after {attempt + 1} attempts: {ex.Message}");
                        return new StreamingConnection(null, ClassifyException(ex));
                    }

                    var delay = policy.GetDelay(attempt + 1);
                    SendMessage("warning", $"{providerName}: Network error, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{maxRetries + 1}): {ex.Message}");

                    await Task.Delay(delay, cancellationToken);
                    attempt++;
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    if (attempt >= maxRetries)
                    {
                        SendMessage("error", $"{providerName}: Timeout after {attempt + 1} attempts");
                        return new StreamingConnection(null, new LlmFailure(LlmFailureKind.Timeout));
                    }

                    var delay = policy.GetDelay(attempt + 1);
                    SendMessage("warning", $"{providerName}: Timeout, retrying in {delay.TotalMilliseconds:F0}ms (attempt {attempt + 1}/{maxRetries + 1})");

                    await Task.Delay(delay, cancellationToken);
                    attempt++;
                }
            }

            return new StreamingConnection(null, LlmFailure.Unknown);
        }

        /// <summary>
        /// Parses SSE (Server-Sent Events) stream format.
        /// </summary>
        protected static async IAsyncEnumerable<string> ParseSseStream(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // An event's data lines are joined with "\n" and the event ends at a blank line (SSE). SH-L150: the space after
            // "data:" is optional; frames without it were dropped. SH-L151: the buffer was built with AppendLine, so the
            // "[DONE]" check on a flushed event could never match.
            using var reader = new StreamReader(stream);
            var data = new List<string>();

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (line.Length == 0)
                {
                    if (data.Count == 0) continue;
                    var payload = string.Join("\n", data);
                    data.Clear();
                    if (payload == "[DONE]") yield break;
                    yield return payload;
                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var value = line.Length > 5 && line[5] == ' ' ? line[6..] : line[5..];
                if (data.Count == 0 && value == "[DONE]") yield break;
                data.Add(value);
            }

            if (data.Count > 0)
            {
                var last = string.Join("\n", data);
                if (last != "[DONE]") yield return last;
            }
        }

        /// <summary>
        /// Parses OpenAI-style streaming response chunks.
        /// </summary>
        protected static async IAsyncEnumerable<string> ParseOpenAiStreamChunks(IAsyncEnumerable<string> sseChunks, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in sseChunks.WithCancellation(cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                JsonElement json;
                try { json = JsonSerializer.Deserialize<JsonElement>(chunk); }
                catch { continue; }

                if (!json.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                    continue;

                // SH-M163: a finish-only or usage-only chunk has no delta.
                if (!choices[0].TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                    continue;

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
            LlmStreamingResponse streamingResponse,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var textBuilder = new System.Text.StringBuilder();
            var toolCalls = new Dictionary<int, (string? Id, string? Name, System.Text.StringBuilder Arguments)>();
            string? finishReason = null;
            TokenUsage? tokenUsage = null;

            await foreach (var chunk in sseChunks.WithCancellation(cancellationToken))
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
                        PromptTokens = ReadTokenCount(usageEl, "prompt_tokens"),
                        CompletionTokens = ReadTokenCount(usageEl, "completion_tokens"),
                        Model = ReadString(json, "model")
                    };
                }

                if (!json.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];

                if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    finishReason = fr.GetString();

                if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
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

                if (delta.TryGetProperty("tool_calls", out var toolCallsDelta) && toolCallsDelta.ValueKind == JsonValueKind.Array)
                {
                    foreach (var toolCallDelta in toolCallsDelta.EnumerateArray())
                    {
                        var index = toolCallDelta.TryGetProperty("index", out var indexEl) && indexEl.TryGetInt32(out var i) ? i : 0;

                        if (!toolCalls.ContainsKey(index))
                            toolCalls[index] = (null, null, new System.Text.StringBuilder());

                        var entry = toolCalls[index];

                        if (ReadString(toolCallDelta, "id") is { } id)
                            entry.Id = id;

                        if (toolCallDelta.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
                        {
                            if (ReadString(function, "name") is { } name)
                                entry.Name = name;
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

            // SH-M161: decided before the tool calls are built — a stream cut off by the length limit has truncated arguments,
            // and running them would report a corrupted call as executed.
            var stopReason = MapFinishReason(finishReason, toolCalls.Count > 0);

            foreach (var (index, (id, name, arguments)) in stopReason == "tool_use" ? toolCalls.OrderBy(kv => kv.Key) : Enumerable.Empty<KeyValuePair<int, (string? Id, string? Name, System.Text.StringBuilder Arguments)>>())
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
