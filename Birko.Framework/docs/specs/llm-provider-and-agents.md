---
area: llm-provider-and-agents
generated-at: 50828c68d6f35ca2096600381c2d0c4481132330
generated-on: 2026-10-21
sources:
  - ../Birko.AI.Agents/Agents/AgentRegistration.cs
  - ../Birko.AI.Agents/Agents/Coding/DebugAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/DocumentationAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/RefactorAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/Specialized/AngularCodingAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/Specialized/AssemblerCodingAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/Specialized/CSharpCodingAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/Specialized/CppCodingAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/Specialized/CssCodingAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/Specialized/HtmlCodingAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/Specialized/JavaScriptTypeScriptCodingAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/Specialized/PhpCodingAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/Specialized/PythonCodingAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/Specialized/ReactCodingAgent.cs
  - ../Birko.AI.Agents/Agents/Coding/TestAgent.cs
  - ../Birko.AI.Agents/Agents/CodingAgent.cs
  - ../Birko.AI.Agents/Agents/DiagrammingAgent.cs
  - ../Birko.AI.Agents/Agents/Media/BitmapAgent.cs
  - ../Birko.AI.Agents/Agents/Media/ImageAgent.cs
  - ../Birko.AI.Agents/Agents/Media/SvgAgent.cs
  - ../Birko.AI.Agents/Agents/MediaAgent.cs
  - ../Birko.AI.Agents/Agents/OrchestratorAgent.cs
  - ../Birko.AI.Contracts/AgentOptions.cs
  - ../Birko.AI.Contracts/LlmProviderFactory.cs
  - ../Birko.AI.Contracts/Models/ContentBlock.cs
  - ../Birko.AI.Contracts/Models/ConversationCheckpoint.cs
  - ../Birko.AI.Contracts/Models/LlmFailure.cs
  - ../Birko.AI.Contracts/Models/LlmResponse.cs
  - ../Birko.AI.Contracts/Models/LlmStreamingResponse.cs
  - ../Birko.AI.Contracts/Models/Message.cs
  - ../Birko.AI.Contracts/Models/MessageText.cs
  - ../Birko.AI.Contracts/Models/TokenUsage.cs
  - ../Birko.AI.Contracts/Providers/ILlmProvider.cs
  - ../Birko.AI.Contracts/Tools/Tool.cs
  - ../Birko.AI.Orchestration/Dispatch/DirectTaskDispatcher.cs
  - ../Birko.AI.Orchestration/Dispatch/ITaskDispatcher.cs
  - ../Birko.AI.Orchestration/Models/AgentTaskRecord.cs
  - ../Birko.AI.Orchestration/Models/AgentTaskStatus.cs
  - ../Birko.AI.Orchestration/Models/EscalationAlert.cs
  - ../Birko.AI.Orchestration/Models/ExecutionMetrics.cs
  - ../Birko.AI.Orchestration/Models/ImplementationPlan.cs
  - ../Birko.AI.Orchestration/Models/TaskAssignment.cs
  - ../Birko.AI.Orchestration/Services/StepDependencyAnalyzer.cs
  - ../Birko.AI.Orchestration/Validation/IStepValidator.cs
  - ../Birko.AI.Orchestration/Validation/StepValidationService.cs
  - ../Birko.AI.Orchestration/Validation/StepValidators.cs
  - ../Birko.AI.Providers/ProviderRegistration.cs
  - ../Birko.AI.Providers/Providers/AzureOpenAiProvider.cs
  - ../Birko.AI.Providers/Providers/ClaudeProvider.cs
  - ../Birko.AI.Providers/Providers/DeepSeekProvider.cs
  - ../Birko.AI.Providers/Providers/GeminiProvider.cs
  - ../Birko.AI.Providers/Providers/GitHubCopilotProvider.cs
  - ../Birko.AI.Providers/Providers/GroqProvider.cs
  - ../Birko.AI.Providers/Providers/LlamaCppProvider.cs
  - ../Birko.AI.Providers/Providers/LmStudioProvider.cs
  - ../Birko.AI.Providers/Providers/MistralProvider.cs
  - ../Birko.AI.Providers/Providers/OllamaProvider.cs
  - ../Birko.AI.Providers/Providers/OpenAiCompatibleProviderBase.cs
  - ../Birko.AI.Providers/Providers/OpenAiProvider.cs
  - ../Birko.AI.Providers/Providers/OpenRouterProvider.cs
  - ../Birko.AI.Providers/Providers/SglangProvider.cs
  - ../Birko.AI.Providers/Providers/VllmProvider.cs
  - ../Birko.AI.Providers/Providers/ZAiProvider.cs
  - ../Birko.AI.Resilience.Data/Models/CircuitBreakerStateModel.cs
  - ../Birko.AI.Resilience.Data/Models/UsageRecordModel.cs
  - ../Birko.AI.Resilience.Data/Stores/StoreCircuitBreakerStore.cs
  - ../Birko.AI.Resilience.Data/Stores/StoreUsageRepository.cs
  - ../Birko.AI.Resilience/Configuration/CostTrackingConfiguration.cs
  - ../Birko.AI.Resilience/Configuration/RateLimitConfiguration.cs
  - ../Birko.AI.Resilience/Services/CostTrackingService.cs
  - ../Birko.AI.Resilience/Services/ProviderCircuitBreaker.cs
  - ../Birko.AI.Resilience/Services/ProviderRateLimiter.cs
  - ../Birko.AI.Resilience/Services/TrackedLlmProvider.cs
  - ../Birko.AI.Resilience/Stores/ICircuitBreakerStore.cs
  - ../Birko.AI.Resilience/Stores/IUsageRepository.cs
  - ../Birko.AI/Agents/Agent.cs
  - ../Birko.AI/Agents/AgentHooks.cs
  - ../Birko.AI/Agents/AgentRunResult.cs
  - ../Birko.AI/Factories/AgentFactory.cs
  - ../Birko.AI/Providers/LlmProviderBase.cs
  - ../Birko.AI/Tools/AppendToFileTool.cs
  - ../Birko.AI/Tools/AskUserTool.cs
  - ../Birko.AI/Tools/DisplayTextTool.cs
  - ../Birko.AI/Tools/EditFileTool.cs
  - ../Birko.AI/Tools/ListFilesTool.cs
  - ../Birko.AI/Tools/ReadFileTool.cs
  - ../Birko.AI/Tools/RunCommandTool.cs
  - ../Birko.AI/Tools/SearchCodeTool.cs
  - ../Birko.AI/Tools/WorkspacePath.cs
  - ../Birko.AI/Tools/WriteFileTool.cs
# source-commits: omitted — every source is inside this repo, so `generated-at` covers them (regen step 5c).
shaped-by: [FEATURE-006, FEATURE-014]
# Evidence pass ran 2026-10-21 (TASK-696) over 376 feature-linked tasks: 117 resolved, 259 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 1 commit leads with the id of a task the state gate refuses. Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 259
---
# LLM provider contract, agent run loop and tool execution

## Purpose

This capability is the framework's LLM integration layer. `Birko.AI.Contracts` declares a
zero-dependency provider contract (`ILlmProvider`), the message/response wire model
(`Message`, `ContentBlock`, `LlmResponse`, `LlmStreamingResponse`, `TokenUsage`), the abstract
`Tool` base class, the agent configuration object (`AgentOptions`) and a registration-based
provider factory. `Birko.AI` builds on it: `LlmProviderBase` supplies the HTTP retry loop,
OpenAI-shaped request/response translation and SSE stream parsing that concrete providers
(Claude, OpenAI, Gemini, Ollama, Azure, …) reuse; `Agent` runs the iterate-call-model /
execute-tools loop; and nine built-in tools give an agent filesystem, search, shell and
user-interaction capabilities inside a confined working directory. Consumers depend on this
layer through the two static registries (`LlmProviderFactory`, `AgentFactory`) so no consumer
needs a compile-time reference to a concrete provider or agent type.

Around that core sit four satellite projects. `Birko.AI.Providers` holds the concrete HTTP providers
(OpenAI, Azure OpenAI, Claude, Gemini, Ollama, Z.AI, GitHub Copilot and an OpenAI-compatible base
shared by llama.cpp, vLLM, SGLang, Mistral, DeepSeek, Groq, LM Studio and OpenRouter) and registers
them with `LlmProviderFactory`. `Birko.AI.Agents` holds the prompt-only agent catalogue (coding,
language-specialised, task, media and orchestrator agents) and registers it with `AgentFactory`.
`Birko.AI.Resilience` decorates a provider with rate limiting and cost/budget tracking and offers a
per-provider circuit breaker; `Birko.AI.Resilience.Data` persists both over any `IAsyncBulkStore`.
`Birko.AI.Orchestration` holds the plan/step model, file-dependency analysis, step validators and an
in-process task dispatcher that multi-agent hosts build on.

## Requirements

### Requirement: Provider contract surface

The system SHALL expose every LLM provider through `ILlmProvider`, which declares a `Name`,
a settable `MessageCallback` of shape `Action<string, string>` (type, content), a
`SendMessageAsync(List<Message>, List<Tool>, string systemPrompt, CancellationToken)` returning
`LlmResponse`, and a `SendMessageStreamingAsync(...)` returning `LlmStreamingResponse`.

#### Scenario: Provider is addressed only through the interface

- **Given** an `Agent` constructed with any `ILlmProvider` implementation
- **When** the agent needs a completion
- **Then** it calls `SendMessageAsync` or `SendMessageStreamingAsync` with the conversation, the
  agent's tool list and `SystemPrompt`, and never depends on the concrete provider type

#### Scenario: An agent without a callback leaves the provider's callback alone

- **Given** a provider whose `MessageCallback` is already set
- **When** an `Agent` is constructed over it with `messageCallback` of `null`
- **Then** the provider keeps its callback (TASK-160, SH-M155; the constructor used to assign `null`). An agent
  constructed with a callback still points the provider at it — a provider has one callback.

### Requirement: Streaming is optional and degrades through an error field

The system SHALL provide a default `LlmProviderBase.SendMessageStreamingAsync` implementation
that returns an `LlmStreamingResponse` whose `Error` is `"Streaming not supported"` and whose
`GetStreamAsync` delegate returns a faulted task carrying
`NotSupportedException($"Streaming is not supported by {Name} provider")`.

#### Scenario: Provider that has not overridden streaming

- **Given** a provider deriving from `LlmProviderBase` without overriding `SendMessageStreamingAsync`
- **When** `SendMessageStreamingAsync` is awaited
- **Then** the returned `LlmStreamingResponse` has `Error = "Streaming not supported"`,
  `FinalResponse = null` and `IsComplete = false`

#### Scenario: Faulted stream delegate is only observed on consumption

- **Given** the default streaming response
- **When** the caller invokes `GetStreamAsync()`
- **Then** the returned task faults with `NotSupportedException`

### Requirement: Streaming response owns its transport resource

The system SHALL let a provider attach the underlying transport handle (typically the streaming
`HttpResponseMessage`) to `LlmStreamingResponse.Resource`, and SHALL dispose that handle exactly
once when the response is disposed, clearing the field and suppressing finalization.
`DisposeAsync` SHALL delegate to `Dispose` and return a completed `ValueTask`.

#### Scenario: Abandoned stream still releases the connection

- **Given** an `LlmStreamingResponse` whose `Resource` is the live HTTP response
- **When** enumeration is abandoned by an exception and the response is disposed
- **Then** `Resource.Dispose()` is invoked and `Resource` is set to `null`

#### Scenario: Double dispose is harmless

- **Given** an `LlmStreamingResponse` already disposed once
- **When** `Dispose()` is called again
- **Then** the null-conditional `Resource?.Dispose()` is a no-op and no exception is thrown

### Requirement: Transient HTTP failures are retried with backoff

The system SHALL retry a non-streaming provider request via
`LlmProviderBase.SendWithRetryAsync` while the response status is retryable and attempts remain.
Retryable statuses SHALL be `TooManyRequests` (429), `InternalServerError` (500), `BadGateway`
(502), `ServiceUnavailable` (503), `GatewayTimeout` (504), `RequestTimeout` (408) and any status
code numerically `>= 500`. The default `RetryPolicy` for a provider SHALL be `MaxRetries = 3`,
`BaseDelay = 1s`, `MaxDelay = 30s`, `AddJitter = true`.

#### Scenario: 503 then success

- **Given** an endpoint returning 503 on the first call and 200 on the second
- **When** `SendWithRetryAsync` is invoked
- **Then** a `"warning"` message reporting the status and delay is emitted, the call is retried
  after the policy delay, and the tuple `(Response, ResponseBody)` for the successful call is returned

#### Scenario: 400 is not retried

- **Given** an endpoint returning `BadRequest`
- **When** `SendWithRetryAsync` is invoked
- **Then** an `"error"` message of the form `"{providerName} API Error ({status}): {detail}"` is
  emitted (or without the detail suffix when no detail could be extracted) and the tuple is
  returned immediately with no retry

#### Scenario: Retries exhausted on a retryable status

- **Given** an endpoint that always returns 429 and a policy with `MaxRetries = 3`
- **When** `SendWithRetryAsync` is invoked
- **Then** four attempts are made and an `"error"` message containing
  `"API Error after 4 attempts"` is emitted before the last response and body are returned

#### Scenario: Retry-After header overrides the computed delay

- **Given** a retryable response carrying `Retry-After: 7`
- **When** the delay before the next attempt is computed
- **Then** `TimeSpan.FromSeconds(7)` is used instead of `RetryPolicy.GetDelay(attempt + 1)`

#### Scenario: Retry-After in the past is ignored

- **Given** a retryable response whose `Retry-After` is an HTTP date already elapsed
- **When** the delay is computed
- **Then** `GetRetryAfterDelay` returns `null` and the policy-computed delay is used

#### Scenario: Network failure after exhausting retries rethrows

- **Given** an endpoint that always throws `HttpRequestException`
- **When** the final attempt fails
- **Then** an `"error"` message `"{providerName}: Network error after N attempts: …"` is emitted
  and the `HttpRequestException` is rethrown to the caller

#### Scenario: Timeout is recognised only through an inner TimeoutException

- **Given** a `TaskCanceledException` whose `InnerException` is a `TimeoutException`
- **When** it surfaces from `HttpClient.SendAsync`
- **Then** it is treated as a retryable timeout; a `TaskCanceledException` from caller
  cancellation (no inner `TimeoutException`) matches no catch clause and propagates immediately

#### Scenario: Policy with negative MaxRetries still sends once

- **Given** a `RetryPolicy` with `MaxRetries = -1`
- **When** `SendWithRetryAsync` or `ConnectStreamingAsync` is invoked
- **Then** the policy is clamped to `MaxRetries = 0` and exactly one request is sent (TASK-173, SH-L148: the loop
  body never ran and `(null, null)` was returned)

### Requirement: The returned HTTP response is already disposed

The system SHALL dispose the `HttpResponseMessage` on every return path of
`SendWithRetryAsync` before returning it, so callers may read only members that remain valid
after disposal (`StatusCode`, `IsSuccessStatusCode`) and SHALL use the separately returned
buffered `ResponseBody` string for content.

#### Scenario: Caller reads the body from the tuple, not the response

- **Given** a successful call through `SendWithRetryAsync`
- **When** the caller inspects the result
- **Then** `ResponseBody` holds the fully buffered body while the returned
  `HttpResponseMessage` has been disposed

### Requirement: Streaming request retry returns null instead of throwing

The system SHALL retry the initial streaming connection via
`SendStreamingWithRetryAsync` using the same retryable-status set and delay computation, but
SHALL signal every terminal failure — non-retryable status, exhausted retries, network error,
timeout — by returning `null` after emitting an `"error"` message, and SHALL NOT rethrow the
underlying exception.

#### Scenario: Non-retryable status on a streaming call

- **Given** a streaming endpoint returning 401
- **When** `SendStreamingWithRetryAsync` is invoked
- **Then** `"{providerName} API Error (Unauthorized): {detail}"` is emitted — or
  `"{providerName} API Error: Unauthorized"` when no detail can be extracted from the body, the status rendered as
  its `HttpStatusCode` name (TASK-173, SH-L146: the provider's explanation used to be dropped) — the response is
  disposed and `null` is returned

#### Scenario: Streaming network error diverges from the non-streaming path

- **Given** repeated `HttpRequestException` up to `MaxRetries`
- **When** `SendStreamingWithRetryAsync` exhausts attempts
- **Then** it returns `null`, whereas `SendWithRetryAsync` under the same conditions rethrows
  the `HttpRequestException`

#### Scenario: Success returns the undisposed response

- **Given** a streaming endpoint returning 200
- **When** `SendStreamingWithRetryAsync` succeeds
- **Then** the live `HttpResponseMessage` obtained with `HttpCompletionOption.ResponseHeadersRead`
  is returned undisposed so the body stream can be read

#### Scenario: ConnectStreamingAsync says why there is no stream

- **Given** any of the terminal failures above
- **When** `ConnectStreamingAsync` (which `SendStreamingWithRetryAsync` now wraps) runs
- **Then** it returns no response and the classified `LlmFailure` (TASK-531); the providers build their failed
  `LlmStreamingResponse` from it with `StreamingFailure`, so `FinalResponse.Failure` carries the classification

### Requirement: A failed call carries a classified failure

The system SHALL set `LlmResponse.Failure` (an `LlmFailure`: `Kind`, `StatusCode`, `RetryAfter`, `IsTransient`,
`Category`) on every error response, classified once in `LlmProviderBase` from the HTTP status, the Retry-After
header and the exception type, never from the error text (TASK-531). 429 is `RateLimited`, 5xx `ServerError`, 408 and
the client's own timeout `Timeout`, a connection failure `Network` (these four are transient); 400 and other 4xx
`BadRequest`, 401 `Unauthorized`, 402 and a 429 whose `error.code` or `error.type` is `insufficient_quota` `Billing`,
403 `Forbidden`, 404 `NotFound`, a missing key `NotConfigured`, an unreadable success body `InvalidResponse`, anything
else `Unknown` (all permanent). `LlmResponse.Error(message)` without a failure is `Unknown`. A Claude error object in
a 200 body is classified by its documented `error.type`. `ErrorCategory` (`Transient = 0`, `Permanent = 1`) and
`LlmFailureKind` have pinned values. Every HTTP provider, sync and streaming, is covered.

The system SHALL NOT answer the caller's cancellation with an error response: every provider's catch-all excludes
an `OperationCanceledException` raised while the caller's token is cancelled, so it propagates.

#### Scenario: A rate limit with Retry-After

- **Given** a provider whose request is answered 429 with `Retry-After: 7` and no retries left
- **When** `SendMessageAsync` returns
- **Then** `Failure.Kind = RateLimited`, `StatusCode = 429`, `IsTransient = true`, `RetryAfter = 7 s`

#### Scenario: Quota exhaustion is billing, not a rate limit

- **Given** a 429 whose body is `{"error":{"code":"insufficient_quota",...}}`
- **When** the call fails
- **Then** `Failure.Kind = Billing` and `IsTransient = false`

#### Scenario: Cancellation is not a failure

- **Given** a request in flight and the caller's token cancelled
- **When** the provider observes the cancellation
- **Then** `SendMessageAsync` / `SendMessageStreamingAsync` throw `OperationCanceledException`; before TASK-531 they
  returned `StopReason = "error"` with "A task was canceled"

#### Scenario: The client's own timeout is transient

- **Given** `HttpClient.Timeout` elapses (a `TaskCanceledException` wrapping `TimeoutException`, the caller's token not
  cancelled)
- **When** the provider catches it
- **Then** `Failure.Kind = Timeout` and `IsTransient = true`

### Requirement: Provider error messages are extracted from the response body

The system SHALL attempt, in `ExtractErrorFromResponseBody`, to read a human-readable message
from a JSON body by probing `error.message`, then `error.msg`, then a string-valued `error`,
then top-level `message`, then a string-valued top-level `detail`; SHALL return `null` when the
body is null/whitespace or when a well-formed JSON object carries none of those; and SHALL
return the raw body truncated to 200 characters plus `"..."` when probing throws.

#### Scenario: OpenAI-shaped error object

- **Given** the body `{"error":{"message":"rate limited","type":"rate_limit_error"}}`
- **When** `ExtractErrorFromResponseBody` is called
- **Then** it returns `"rate limited"`

#### Scenario: Non-JSON body

- **Given** a 500 body of HTML text longer than 200 characters
- **When** `ExtractErrorFromResponseBody` is called
- **Then** deserialization throws, the catch block returns the first 200 characters followed by `"..."`

#### Scenario: JSON scalar body

- **Given** the body `"oops"` (a valid JSON string, not an object)
- **When** `TryGetProperty` is attempted on it
- **Then** the resulting `InvalidOperationException` is caught and the raw body is returned

#### Scenario: JSON object with no recognised error field

- **Given** the body `{"status":"failed"}`
- **When** `ExtractErrorFromResponseBody` is called
- **Then** it returns `null` and callers emit the status-only error message form

### Requirement: Conversation is translated into OpenAI chat-completion shape

The system SHALL build, in `BuildOpenAiStyleMessages`, a list whose first entry is
`{role="system", content=systemPrompt}` followed by one or more entries per `Message`, mapping
content shapes as follows: a `List<ContentBlock>` on an `assistant` turn containing `tool_use`
blocks becomes a single entry with `tool_calls` (each `{id, type="function", function={name,
arguments}}`, `arguments` being the serialized `Input`); a block list with only text blocks
becomes the single text or the newline-join of all text blocks; a block list with no text blocks
becomes the empty string; and any other content is passed through as-is.

#### Scenario: Assistant turn with one tool call

- **Given** an assistant `Message` whose `Content` is `[ContentBlock{Type="tool_use", Id="c1",
  Name="read_file", Input={file_path:"a.cs"}}]`
- **When** `BuildOpenAiStyleMessages` runs
- **Then** one entry is emitted with `role="assistant"`, `content=null` and one `tool_calls`
  element whose `function.arguments` is `{"file_path":"a.cs"}`

#### Scenario: Assistant turn with several text blocks alongside a tool call

- **Given** content `[text "first", text "second", tool_use …]` on an `assistant` turn
- **When** the tool-call branch is taken
- **Then** `content` is `"first\nsecond"`, joined as the non-tool branch joins them (TASK-160, SH-M165; only the
  first was carried)

#### Scenario: Tool-result turn expressed as anonymous objects

- **Given** a `user` `Message` whose `Content` is a list of anonymous objects each with `type`,
  `tool_use_id` and `content` properties (as produced by the agent loop)
- **When** `BuildOpenAiStyleMessages` runs
- **Then** one `{role="tool", tool_call_id, content}` entry is emitted per object and the
  original message contributes no other entry

#### Scenario: Tool results given as dictionaries or JsonElements

- **Given** tool-result items that are `Dictionary<string, object?>` or `JsonElement` objects instead of anonymous objects
- **When** `BuildOpenAiStyleMessages` runs
- **Then** they are read like the anonymous ones (TASK-160, SH-M166; they produced no entry)

#### Scenario: Object list whose first item has `type` but no tool-result pair

- **Given** a list of objects carrying `type` but no `tool_use_id`
- **When** the branch is entered
- **Then** `NotSupportedException` is thrown (rule 41): the message used to be dropped silently, leaving its
  tool_use unanswered

#### Scenario: Object list whose first item lacks `type`

- **Given** a list of plain anonymous objects with no `type` property
- **When** the branch check fails
- **Then** the raw object list is emitted as the `content` of a single `{role, content}` entry

#### Scenario: A message with no role is refused with its position

- **Given** a conversation whose third message has a null or empty `Role`
- **When** `BuildOpenAiStyleMessages` runs
- **Then** `InvalidOperationException("Message 2 has no role.")` is thrown (TASK-173, SH-L154: it went out as
  `"role": null` and the API refused the whole request without naming the turn)

#### Scenario: Block types are matched exactly

- **Given** an assistant `ContentBlock` whose `Type` is `"Tool_Use"`
- **When** `BuildOpenAiStyleMessages` runs
- **Then** it is neither a text block nor a tool call, as the agent's own dispatch treats it (TASK-173, SH-L153);
  `ConvertJsonElementMessage`, by contrast, lower-cases a `JsonElement` block's `type` before matching

### Requirement: JsonElement content is converted with tool-result precedence

The system SHALL convert a `JsonElement` array content, in `ConvertJsonElementMessage`, by
collecting `text`, `tool_use` and `tool_result` elements and then applying a fixed precedence:
if any `tool_result` was collected, only `{role="tool", tool_call_id, content}` entries are
emitted; otherwise if `tool_use` elements were collected and the role is `assistant`, a single
`tool_calls` entry is emitted; otherwise if text was collected, a newline-joined text entry is
emitted; otherwise the conversion reports failure. A `tool_use` element without an `id` SHALL be
assigned a generated `call_{guid:N}` id, and a missing `input` SHALL become `"{}"`.

#### Scenario: Array containing both tool_result and tool_use

- **Given** a `JsonElement` array with one `tool_result` and one `tool_use`
- **When** conversion runs
- **Then** only the `role="tool"` entry is emitted and the `tool_use` is discarded

#### Scenario: tool_use on a non-assistant role

- **Given** a `user` message whose `JsonElement` array holds only a `tool_use` element
- **When** conversion runs
- **Then** the `tool_calls` branch is skipped (role is not `assistant`), no text exists, so
  conversion returns `false` and the caller falls back to emitting the array's raw JSON text as content

#### Scenario: Non-array JsonElement

- **Given** content that is a `JsonElement` of kind `String`
- **When** conversion is attempted
- **Then** it returns `false` immediately and the caller uses `GetString()` as the content

### Requirement: Tools are advertised as OpenAI function definitions

The system SHALL project each `Tool` into `{type="function", function={name, description,
parameters}}` where `parameters` is the tool's `InputSchema` object verbatim.

#### Scenario: Built-in tool projection

- **Given** a `ReadFileTool`
- **When** `BuildOpenAiStyleTools` runs
- **Then** the emitted function has `name = "read_file"`, the tool's `Description`, and
  `parameters` equal to the anonymous JSON-schema object declared by `InputSchema`

### Requirement: OpenAI-style responses are parsed into a canonical LlmResponse

The system SHALL parse, in `ParseOpenAiStyleResponse`, a chat-completion body into an
`LlmResponse` (a `text` block from a string `message.content`, then one `tool_use` block per call) whose
`StopReason` comes from `MapFinishReason(choices[0].finish_reason, hasToolCalls)`, shared with the streaming parser:
`length` is `"max_tokens"` and `content_filter` stays itself (a call cut off by the limit is not returned), otherwise
tool calls make it `"tool_use"`, and `stop`, none or a tool-call reason without a call is `"end_turn"` (TASK-160,
SH-M160: `finish_reason` was never read). Every probe is shape-guarded (SH-M157..M159): `"error": null`,
`"tool_calls": null` and a null, string or fractional token count do not discard the completion; a token count is
read by `ReadTokenCount` (a whole-valued fraction rounds, anything else is 0). An `error` object or string SHALL yield
`LlmResponse.Error($"API returned error: {type} - {message}")`, and any thrown exception during
parsing SHALL yield `LlmResponse.Error($"Error parsing OpenAI-style response: {ex.Message}")`.
Both error paths SHALL also invoke the supplied message callback with type `"error"`, and both SHALL carry
`Failure.Kind = InvalidResponse` (permanent) — including an `error` object whose `type` names a rate limit.

#### Scenario: Tool call response

- **Given** a body whose `choices[0].message.tool_calls` holds one entry with
  `function.name = "list_files"` and `arguments = "{\"recursive\":true}"`
- **When** `ParseOpenAiStyleResponse` runs
- **Then** `StopReason` is `"tool_use"` and `Content[0]` is a `tool_use` block whose `Input`
  contains `recursive`

#### Scenario: Plain text response

- **Given** a body with `choices[0].message.content = "done"` and no `tool_calls`
- **When** parsing runs
- **Then** `StopReason` is `"end_turn"` and `Content[0]` is `{Type="text", Text="done"}`

#### Scenario: Malformed tool arguments abort the whole parse

- **Given** `function.arguments = "{not json"`
- **When** parsing reaches `JsonSerializer.Deserialize<Dictionary<string, object>>`
- **Then** the exception is caught by the outer handler and the whole response becomes
  `LlmResponse.Error("Error parsing OpenAI-style response: …")` — unlike the streaming parser,
  which preserves the raw arguments under an `_raw` key

#### Scenario: Missing choices array

- **Given** a body with neither `error` nor `choices` (or a `choices` that is not a non-empty array)
- **When** the parser throws `JsonException("The response has no choices.")`
- **Then** the result is an error `LlmResponse` with `StopReason = "error"` and `Failure.Kind = InvalidResponse`

#### Scenario: Usage and model capture

- **Given** a body carrying `usage.prompt_tokens = 12`, `usage.completion_tokens = 5` and
  top-level `model = "gpt-4o"`
- **When** parsing runs
- **Then** `Usage` is populated with those counts, `Usage.Model = "gpt-4o"` and
  `Usage.TotalTokens` computes to 17; absent sub-properties default to 0 and `Model` to `null`

### Requirement: Not-configured providers return a distinct stop reason

The system SHALL provide `LlmProviderBase.NotConfigured()` producing an `LlmResponse` with
`StopReason = "NotConfigured"`, empty `Content`, `ErrorMessage = "Provider is not properly
configured. Check API keys and settings."` and `Failure.Kind = NotConfigured` (TASK-531), and SHALL declare an abstract `IsConfigured()` that
derived providers implement.

#### Scenario: Agent reaction to NotConfigured

- **Given** a provider returning `NotConfigured()`
- **When** the agent handles the response
- **Then** it emits `"error"` with `"Provider '{name}' is not properly configured."`, appends
  that text to the assistant turn and stops the loop

#### Scenario: IsConfigured is never called by the base class

- **Given** `LlmProviderBase`
- **When** `SendWithRetryAsync` or `SendStreamingWithRetryAsync` run
- **Then** neither consults `IsConfigured()`; enforcing configuration is left to the derived provider

### Requirement: SSE frames are decoded into data chunks

The system SHALL decode a Server-Sent-Events stream in `ParseSseStream` per the SSE grammar (TASK-173): every
`data:` line (the space after the colon optional, matched ordinally — SH-L150) adds a line to the event, an event's
lines are joined with `\n` and yielded on a blank line, a `[DONE]` event ends the enumeration however it is framed
(SH-L151: the old buffer ended in a newline, so the flushed check never matched), and a residual event is yielded when
the reader ends.

#### Scenario: Single-line frame

- **Given** the stream `data: {"a":1}\n\n`
- **When** it is parsed
- **Then** one chunk `{"a":1}` is yielded, with no trailing newline

#### Scenario: Data line without the optional space

- **Given** the stream `data:{"a":1}\n\n`
- **When** it is parsed
- **Then** the chunk `{"a":1}` is yielded (it used to be dropped)

#### Scenario: Sentinel terminates the stream

- **Given** a stream whose next line is `data: [DONE]`
- **When** parsing reaches it
- **Then** the enumeration ends without yielding and any subsequent bytes are ignored

#### Scenario: Stream ends without a trailing blank line

- **Given** the stream `data: {"a":1}` with no terminating blank line
- **When** the reader returns `null`
- **Then** the buffered chunk is yielded after the loop

### Requirement: Streaming chunks are accumulated with tool-call capture

The system SHALL, in `ParseOpenAiStreamChunksWithToolCapture`, yield each non-empty
`choices[0].delta.content` string while accumulating it, accumulate `delta.tool_calls` fragments
keyed by their `index` (id, name and appended `arguments`), capture `usage` and `finish_reason`
from any chunk, and on completion populate the `LlmStreamingResponse` with `FinalResponse`
(a text block first when text was accumulated, then one `tool_use` block per captured index in
ascending index order), `StopReason`, `AccumulatedText`, `Usage` and `IsComplete = true`.
The derived stop reason SHALL be `MapFinishReason(finish_reason, anyToolCall)` (above), decided before the tool calls
are built, so a stream cut off by the length limit returns no truncated call (TASK-160, SH-M161: it was `tool_use`
and the call ran with `{"_raw": …}`). A chunk with no `delta`, a null `choices` or `tool_calls`, or an odd usage count
SHALL be skipped or read as 0, never thrown mid-stream (SH-M162, SH-M163).

#### Scenario: Fragmented tool arguments are reassembled

- **Given** chunks delivering `arguments` fragments `{"pa`, `th":"a.cs"`, `}` at index 0
- **When** the stream completes
- **Then** `FinalResponse.Content` holds one `tool_use` block whose `Input` contains `path = "a.cs"`
  and `StopReason` is `"tool_use"`

#### Scenario: Unparseable accumulated arguments fall back to a raw key

- **Given** accumulated `arguments` that are not valid JSON
- **When** deserialization fails
- **Then** `Input` becomes `{"_raw": "<the raw argument string>"}` and streaming still completes

#### Scenario: Truncated completion surfaces its finish reason verbatim

- **Given** a stream whose final `finish_reason` is `"length"`, with or without tool calls
- **When** the stream completes
- **Then** `StopReason` is `"max_tokens"` and no tool call is returned; the agent's stop-reason switch ends the run
  with the `Truncated` outcome (TASK-660)

#### Scenario: Unparseable chunk is skipped

- **Given** an SSE payload that is not valid JSON
- **When** the parser deserializes it
- **Then** the chunk is skipped and enumeration continues

#### Scenario: Chunk without a delta

- **Given** a chunk with a non-empty `choices` array whose element has no `delta` property
- **When** the tool-capture parser reads it
- **Then** the `TryGetProperty("delta")` guard skips it, and the simpler `ParseOpenAiStreamChunks` skips it the
  same way (TASK-160, SH-M163: it called `GetProperty("delta")` and threw `KeyNotFoundException` out of the enumerator)

### Requirement: Message text is read shape-independently

The system SHALL expose `MessageText.From(object?)` (and `Message.GetText()`) as the canonical
way to read a message's plain text: a `string` content returns itself; a single `ContentBlock`
returns its `Text` only when `Type == "text"`; an `IEnumerable<ContentBlock>` returns the
concatenation of every non-null `text` block's `Text`; a `JsonElement` (TASK-535) is read as the string or
block array it holds; and `null` or any unrecognised shape returns `string.Empty`.

#### Scenario: Assistant block list

- **Given** content `[text "Hello ", tool_use …, text "world"]`
- **When** `GetText()` is called
- **Then** it returns `"Hello world"`

#### Scenario: Tool-use-only assistant turn

- **Given** content consisting solely of `tool_use` blocks
- **When** `GetText()` is called
- **Then** it returns `string.Empty`

#### Scenario: Unrecognised content shape

- **Given** content that is a `List<object>` of anonymous tool-result objects
- **When** `GetText()` is called
- **Then** the `default` branch returns `string.Empty`

#### Scenario: Content deserialized as a JsonElement

- **Given** a `Message` read by `JsonSerializer`, whose `Content` is a `JsonElement` string or block array
- **When** `GetText()` is called
- **Then** it returns the text (before TASK-535 it returned `string.Empty`)

### Requirement: A conversation is checkpointed in its in-memory shapes and trimmed without splitting tool pairs

The system SHALL provide `ConversationCheckpoint.Serialize` / `Deserialize` (TASK-535), a versioned JSON
(`{"version":1,"messages":[…]}`) that restores a string content as a string, text and tool_use blocks as
`List<ContentBlock>` (tool input values as `JsonElement`), and tool_result blocks as the agent's anonymous
`{ type, tool_use_id, content }` objects, so a restored conversation resumes through `Agent.ResumeAsync` and every
provider sends it as it would a live one. A checkpoint of another version SHALL be refused with `JsonException`.
`ConversationTrimmer.Trim(messages, max)` (max ≥ 2) SHALL keep the first user message and a tail that starts at an
assistant turn, so no tool_result is kept without its tool_use and no two user turns become adjacent.

#### Scenario: Resume a restored conversation

- **Given** a checkpoint of `user, assistant tool_use t1, user tool_result t1`, restored
- **When** an agent resumes it on an OpenAI-compatible provider and on Claude
- **Then** the request carries `tool_calls[t1]` then a `tool` message for `t1` (OpenAI), or the `tool_use` and
  `tool_result` blocks for `t1` (Claude)

#### Scenario: Trim at every size

- **Given** a 22-message conversation of ten tool rounds
- **When** it is trimmed to each limit from 2 to 23
- **Then** the first user message and the last message are kept and every kept tool_result follows an assistant turn

### Requirement: Agent options carry defaults, clone deeply and merge only what the other set

The system SHALL default `AgentOptions` to `Interactive = true`, `MaxIterations = 10`,
`MaxIterationsPerStep = 10`, `Verbose = true`, `WorkingDirectory = "./"`,
`PromptTimeout = 300`, `ModelDepth = 5`, `EnableStreaming = false`,
`StreamingFallbackToSync = true`, an empty `AllowedExternalPaths`
and a null `DefaultPromptResponse`. `Clone()` SHALL copy every field and allocate a new
`AllowedExternalPaths` list. `Merge(other)` SHALL return immediately on a null `other` and copy a field only when
`other` set it: a value-typed field or `WorkingDirectory` only when it differs from the default, and
`DefaultPromptResponse`, `AllowedExternalPaths` and `OnLlmResponseReceived` only when non-empty/non-null (TASK-160,
SH-M171: scalars were copied unconditionally, so merging options that set only a working directory reset
`MaxIterations`, `Verbose`, `Interactive` and `PromptTimeout`). `Merge` therefore cannot set a field back to its default.

#### Scenario: Clone isolates the path list

- **Given** options with `AllowedExternalPaths = ["C:\\shared"]`
- **When** `Clone()` is called and the clone's list is mutated
- **Then** the original list is unaffected

#### Scenario: Merging a default instance changes nothing

- **Given** options with `MaxIterations = 50` and `Verbose = false`
- **When** `Merge(new AgentOptions())` is called
- **Then** `MaxIterations` stays 50 and `Verbose` stays `false`, because a field the other instance left at its
  default is "not set" (TASK-160, SH-M171: both used to be reset to their defaults)

#### Scenario: Merge cannot restore a default

- **Given** options with `MaxIterations = 50`
- **When** `Merge` is called with an `other` whose `MaxIterations` is explicitly 10 (the default)
- **Then** `MaxIterations` stays 50; restoring a default needs a direct assignment

#### Scenario: Merging preserves a working directory the caller did not set

- **Given** options with `WorkingDirectory = "C:\\repo"`
- **When** `Merge` is called with an `other` whose `WorkingDirectory` is empty
- **Then** `"C:\\repo"` is retained

### Requirement: Options round-trip through a string dictionary tolerantly

The system SHALL build `AgentOptions` from a `Dictionary<string, string>` in `FromDictionary`
using `TryParse` for every typed field, so an absent key or an unparseable value leaves the
default in place rather than throwing. `allowedExternalPaths` SHALL be split on `'\n'`, with an
empty string yielding an empty list. `ToDictionary` SHALL emit every field except
`DefaultPromptResponse`, which SHALL be emitted only when non-null. A key no field reads —
including `checkpointInterval`, whose option was removed — SHALL be ignored.

#### Scenario: Removed checkpointInterval key is ignored

- **Given** the config `{"checkpointInterval": "3", "maxIterations": "7", "enableStreaming": "True"}`
- **When** `FromDictionary` runs
- **Then** no exception is thrown, `MaxIterations` is 7 and `EnableStreaming` is `true`

#### Scenario: Malformed numeric value is ignored

- **Given** the config `{"maxIterations": "ten"}`
- **When** `FromDictionary` runs
- **Then** no exception is thrown and `MaxIterations` remains 10

#### Scenario: Empty allowed-paths value

- **Given** the config `{"allowedExternalPaths": ""}`
- **When** `FromDictionary` runs
- **Then** `AllowedExternalPaths` is an empty list rather than a list holding one empty string

#### Scenario: Blank working directory keeps the default

- **Given** the config `{"workingDirectory": "  "}`
- **When** `FromDictionary` runs
- **Then** `WorkingDirectory` remains `"./"` (TASK-173, SH-L162: the empty value replaced it and every file tool
  then failed); a null `config` is an `ArgumentNullException`

#### Scenario: Null default prompt response is omitted

- **Given** options with `DefaultPromptResponse = null`
- **When** `ToDictionary` runs
- **Then** the result contains no `"defaultPromptResponse"` key

### Requirement: Agent construction wires tools and callbacks

The system SHALL throw `ArgumentNullException` from the `Agent` constructor when
`llmProvider` is null, SHALL substitute a fresh `AgentOptions` when `options` is null, SHALL
build the tool list via the overridable `CreateTools()` and SHALL assign the agent's message
callback and options to every tool. `AddTool` SHALL refuse null and apply the same wiring to a late-added tool;
`RebuildTools()` SHALL rediscover the tool list and re-wire it; `RemoveTool(name)` SHALL remove
the first tool whose `Name` matches exactly and return whether one was removed. Both SHALL replace the list rather
than mutate it (TASK-173, SH-L158), so a change applies from the next model call and never alters the list a call in
flight was given.

#### Scenario: Default tool catalogue

- **Given** an `Agent` subclass that does not override `CreateTools`
- **When** it is constructed
- **Then** `Tools` contains nine tools named `list_files`, `read_file`, `write_file`,
  `edit_file`, `append_to_file`, `search_code`, `run_command`, `display_text`, `ask_user`

#### Scenario: Null provider

- **Given** `llmProvider = null`
- **When** the constructor runs
- **Then** `ArgumentNullException` is thrown for `llmProvider`

#### Scenario: Tool list changes never touch a list already handed out

- **Given** a reference to `agent.Tools` taken before `AddTool(t)` or `RemoveTool(name)`
- **When** the change is made
- **Then** the earlier reference is unchanged and the next model call receives the new list; `AddTool(null)` throws
  `ArgumentNullException` (TASK-173, SH-L158)

#### Scenario: Fewer than one iteration is refused

- **Given** `Options.MaxIterations = 0`, or `RunAsync(task, maxIterations: 0)`
- **When** the run starts
- **Then** `ArgumentOutOfRangeException` is thrown and no provider call is made

#### Scenario: Removing an unknown tool

- **Given** an agent whose tools do not include `"web_fetch"`
- **When** `RemoveTool("web_fetch")` is called
- **Then** it returns `false` and the tool list is unchanged

#### Scenario: Callback change propagates

- **Given** an agent constructed with a null callback
- **When** `SetMessageCallback(cb)` is called
- **Then** `cb` is assigned to the provider and to every tool, and each tool's `Options` is
  re-pointed at the agent's options

### Requirement: Depth guidance is derived from ModelDepth bands

The system SHALL derive the system-prompt reasoning guidance from `Options.ModelDepth` in
three bands: `<= 3` yields the "Quick and efficient" text, `>= 7` yields the "Deep and
thorough" text, and every other value yields the "Balanced" text. Derived agents MAY override
`GetDepthGuidance()`.

#### Scenario: Low depth

- **Given** `ModelDepth = 2`
- **When** `GetDepthGuidance()` is called
- **Then** the returned text begins with `"Reasoning approach: Quick and efficient"`

#### Scenario: Default depth

- **Given** the default `ModelDepth = 5`
- **When** `GetDepthGuidance()` is called
- **Then** the returned text begins with `"Reasoning approach: Balanced"`

### Requirement: Agent runs a bounded call-and-tool loop

The system SHALL refuse a run of fewer than one iteration with `ArgumentOutOfRangeException` (TASK-173, SH-L160: it
returned the seed conversation with no model call), and SHALL iterate from 1 to `maxIterations ?? Options.MaxIterations` in one loop shared
by the streaming and non-streaming paths, firing the iteration hook (when `Hooks` is set) and
then checking cancellation at the top of every iteration, sending the whole conversation plus
tools plus `SystemPrompt` to the provider, invoking `Options.OnLlmResponseReceived` once per completed response — for
streaming after the stream has been read, never for a stream that failed (TASK-173, SH-L156) —
appending the assistant turn to the conversation, then dispatching on the stop reason. When the
loop finishes without a terminal stop reason it SHALL emit a `"warning"` (when `Verbose`) stating
the maximum was reached and return the conversation with the `MaxIterationsReached` outcome. That exit is not
reached today: a `tool_use` turn on the last iteration already ends the run as `MaxIterationsReached` (below).
The iteration message SHALL carry a `" (streaming)"` suffix on the streamed call.

#### Scenario: RunAsync seeds a single user turn

- **Given** `RunAsync("fix the build")`
- **When** the loop starts
- **Then** the conversation begins with one `{Role="user", Content="fix the build"}` message and
  the returned list is that same conversation including every appended turn

#### Scenario: ContinueAsync copies prior history

- **Given** an existing history list and `ContinueAsync(history, "and now add tests")`
- **When** the loop runs
- **Then** a new list is built from `history` with the new user turn appended, and the caller's
  original list is not mutated

#### Scenario: ResumeAsync continues a saved conversation as it stands

- **Given** a saved conversation ending in a tool-result `user` turn
- **When** `ResumeAsync(saved)` runs
- **Then** the first provider call receives exactly the saved turns with no user turn added, the
  caller's list is copied rather than mutated, and the model's reply is appended to the copy

#### Scenario: ResumeAsync refuses a conversation with nothing to answer

- **Given** an empty conversation, or one whose last turn has `Role = "assistant"`
- **When** `ResumeAsync` is called
- **Then** `ArgumentException` is thrown and no provider call is made

#### Scenario: Explicit iteration cap overrides options

- **Given** `Options.MaxIterations = 10`
- **When** `RunAsync(task, maxIterations: 2)` is called and the model keeps requesting tools
- **Then** at most two provider calls are made and the loop reports the maximum reached

#### Scenario: Cancellation between iterations

- **Given** a token cancelled after the first response
- **When** the next iteration begins
- **Then** `cancellationToken.ThrowIfCancellationRequested()` throws `OperationCanceledException`
  out of `RunAsync`

#### Scenario: Verbose progress messages

- **Given** `Verbose = true`
- **When** each iteration starts and each response arrives
- **Then** `"info"` messages `"ITERATION {n}"` and `"Stop reason: {reason}"` are emitted

### Requirement: Streaming applies to the first iteration only, with sync fallback

The system SHALL, when `Options.EnableStreaming` is true, use `SendMessageStreamingAsync` for
iteration 1 only; every later iteration SHALL use `SendMessageAsync`. When iteration 1's streaming
request fails — opening the stream or reading it — with anything other than
`OperationCanceledException`, and `Options.StreamingFallbackToSync` is true, the system SHALL emit
a `"warning"` and retry **that one model call** with `SendMessageAsync`, then continue the run. The
fallback SHALL NOT cover a later iteration, a tool or a hook: those failures propagate, and no
iteration, tool or hook runs twice (TASK-544 — the fallback used to replay the whole run from a
snapshot, re-executing every tool). A non-empty `LlmStreamingResponse.Error` SHALL be reported as `"error"` and
converted into `InvalidOperationException($"Streaming failed: {error}")`.

#### Scenario: Provider without streaming support falls back

- **Given** `EnableStreaming = true`, `StreamingFallbackToSync = true` and a provider using the
  default `SendMessageStreamingAsync`
- **When** `RunAsync` executes
- **Then** the streaming attempt throws `InvalidOperationException`, a `"warning"` beginning
  `"Streaming failed:"` is emitted and iteration 1 is answered by `SendMessageAsync`; the
  iteration hook fired once and nothing was appended to the conversation by the failed attempt

#### Scenario: Fallback disabled propagates the failure

- **Given** `EnableStreaming = true` and `StreamingFallbackToSync = false`
- **When** streaming fails
- **Then** the exception propagates out of `RunAsync` and no synchronous attempt is made

#### Scenario: Cancellation is never converted into a fallback

- **Given** `EnableStreaming = true` and a cancelled token
- **When** the streaming loop throws `OperationCanceledException`
- **Then** the `when` clause excludes it and the exception propagates unchanged

#### Scenario: A hook failure is never converted into a fallback

- **Given** `EnableStreaming = true`, `StreamingFallbackToSync = true` and an iteration or tool
  hook that throws
- **When** the streaming loop throws `AgentHookException`
- **Then** the exception propagates, the hook ran once and no tool is executed a second time

#### Scenario: A failure after the streaming call is never converted into a fallback

- **Given** `EnableStreaming = true`, `StreamingFallbackToSync = true` and a tool executed in
  iteration 1
- **When** iteration 2's `SendMessageAsync` throws, or the tool itself throws
- **Then** the exception propagates out of `RunAsync`, the tool executed once and every hook
  fired once per iteration and tool call

#### Scenario: Chunks are surfaced as they arrive

- **Given** a provider that streams text
- **When** chunks are enumerated
- **Then** each chunk is emitted as an `"assistant_stream"` message and appended to the local text buffer

#### Scenario: Missing FinalResponse is synthesised

- **Given** a stream that completes without the provider setting `FinalResponse`
- **When** the iteration finishes
- **Then** a response is synthesised with `StopReason = streamingResponse.StopReason ?? "end_turn"`
  and a single text block holding the accumulated chunks

#### Scenario: Second iteration is not streamed

- **Given** `EnableStreaming = true` and a first response with `StopReason = "tool_use"`
- **When** iteration 2 runs
- **Then** the non-streaming `SendMessageAsync` is used and no `"assistant_stream"` messages are emitted

### Requirement: Stop reasons drive loop termination

The system SHALL dispatch on `LlmResponse.StopReason`: `"tool_use"` executes the requested tools
and normally continues; `"end_turn"` emits every text block as `"assistant_final"` and stops;
`"error"` emits `"LLM request failed: {ErrorMessage ?? "Unknown LLM error"}"` and stops;
`"NotConfigured"` emits the provider-not-configured error and stops; `"max_tokens"` emits the `"warning"`
`"The model's output was cut off by its token limit. Stopping."` when `Verbose` and stops; any other value (including
`null`) emits a `"warning"` `"Unexpected stop reason: {reason ?? "unknown"}. Stopping."` when `Verbose` and stops. For
the `"error"` and `"NotConfigured"` cases the system SHALL ensure the assistant turn carries the error text.

The system SHALL report how the run ended (TASK-660, decided by the user: a result type from new overloads).
`RunWithOutcomeAsync` / `ContinueWithOutcomeAsync` / `ResumeWithOutcomeAsync` return an `AgentRunResult(Conversation,
Outcome, Failure, StopReason, Detail)`, with the outcome set where the loop decides to stop:

| Ending | `AgentRunOutcome` | Carried |
|---|---|---|
| `"end_turn"` | `Completed` | |
| `"error"` | `LlmFailed` | the response's classified `LlmFailure`, the error message |
| `"NotConfigured"` | `NotConfigured` | the failure, if any |
| `"tool_use"` on the last iteration | `MaxIterationsReached` | |
| `"max_tokens"` | `Truncated` | |
| `"tool_use"` with no tool call | `MissingToolCall` | |
| an iteration hook's `Stop` | `StoppedByHook` | the hook's reason; no stop reason |
| any other value | `UnexpectedStop` | the stop reason |

`RunAsync` / `ContinueAsync` / `ResumeAsync` SHALL return the same conversation as the outcome methods; the outcome is
never written into the conversation (an unexpected stop injects no text, whatever `Verbose` is). Cancellation throws.

#### Scenario: A failed call is told apart from a completed one

- **Given** a provider answering `LlmResponse.Error("slow down", new LlmFailure(RateLimited, 429, 30 s))`
- **When** `RunWithOutcomeAsync` runs
- **Then** the outcome is `LlmFailed`, `Failure` is that failure (transient) and `Detail` is `"slow down"`

#### Scenario: An unexpected stop leaves the conversation as it was

- **Given** a response with `StopReason = "content_filter"`
- **When** the run is made through `RunAsync` and through `RunWithOutcomeAsync`
- **Then** both conversations are equal, and the result's outcome is `UnexpectedStop` with `StopReason = "content_filter"`

#### Scenario: A hook stop before any model call carries no stop reason

- **Given** an iteration hook that calls `Stop("budget")` at iteration 1
- **When** `RunWithOutcomeAsync` runs
- **Then** the outcome is `StoppedByHook`, `Detail = "budget"`, `StopReason` and `Failure` are null and `IsCompleted`
  is false; `IsCompleted` is true only for `Completed`

#### Scenario: The outcome methods share the refusals of their conversation siblings

- **Given** an empty conversation
- **When** `ResumeWithOutcomeAsync` is called
- **Then** `ArgumentException` is thrown, as `ResumeAsync` throws; `ContinueWithOutcomeAsync` copies the caller's
  history as `ContinueAsync` does

#### Scenario: Error response with no content gets error text injected

- **Given** an `"error"` response whose `Content` is empty
- **When** `EnsureErrorContent` runs
- **Then** a text block `"Error: LLM request failed: …"` is added and the last conversation
  message is replaced with an assistant turn holding that content

#### Scenario: Error response that already has text is left alone

- **Given** an `"error"` response whose `Content` already contains a `text` block
- **When** `EnsureErrorContent` runs
- **Then** no block is added and the conversation's last message is not replaced

#### Scenario: Unknown stop reason stops silently when not verbose

- **Given** `Verbose = false` and a response with `StopReason = "content_filter"`
- **When** the switch reaches its default branch
- **Then** the loop stops and no message is emitted

### Requirement: Tool use is executed sequentially and reported back as tool_result

The system SHALL, for each `tool_use` block of a response, check cancellation, emit a
`"tool_call"` message containing the tool name and serialized input, resolve the tool by exact
`Name` match, execute it with `Options.WorkingDirectory` and the block's `Input` (an empty
dictionary when `Input` is null), emit a `"tool_result"` message whose body is truncated to 500
characters plus `"..."`, and collect `{type="tool_result", tool_use_id, content}`. After the
loop it SHALL append one `user` message carrying the collected results, then continue unless the
current iteration has reached the maximum.

#### Scenario: Unknown tool name

- **Given** a `tool_use` block naming a tool the agent does not hold
- **When** the block is handled
- **Then** the result is the string `"Error: Unknown tool '{name}'"`, it is still reported back
  as a `tool_result`, and the error counter is incremented

#### Scenario: Long tool output is truncated in the emitted message only

- **Given** a tool returning 4000 characters
- **When** the result is reported
- **Then** the `"tool_result"` message carries the first 500 characters plus `"..."` while the
  full result is placed in the `tool_result` content sent back to the model

#### Scenario: Iteration budget reached during tool use

- **Given** the current iteration equals the maximum
- **When** tool results have been appended
- **Then** a `"warning"` stating `"Maximum iterations ({n}) reached. Task may be incomplete."` is
  emitted and the loop stops, leaving the tool results as the final turn

#### Scenario: Every tool failed

- **Given** three tool results all starting with `"Error:"` and `Verbose = true`
- **When** the batch completes and iterations remain
- **Then** a `"warning"` `"All tool executions failed. Giving agent one final response…"` is
  emitted and the loop continues

#### Scenario: tool_use response with no tool_use blocks

- **Given** a response with `StopReason = "tool_use"` whose `Content` holds only text blocks
- **When** the handler runs
- **Then** a `"warning"` is emitted, the text blocks are emitted as `"assistant_final"` and the run ends (TASK-160,
  SH-M154: an empty tool_result turn was appended, which providers reject on the next call)

#### Scenario: Tool exceptions are not caught by the agent

- **Given** a custom tool whose `ExecuteAsync` throws
- **When** the agent executes it
- **Then** the same exception propagates out of `RunAsync` — immediately when no `Hooks` are set,
  and after `OnToolCallFinishedAsync` has observed it when they are

### Requirement: An iteration hook can append a user message or stop the run

The system SHALL read `Agent.Hooks` (an `IAgentHooks`; `AgentHooks` supplies no-op defaults)
once at the start of each run and, when it is set, invoke `OnIterationStartingAsync` before
every model call and before the run's cancellation check, with an `AgentIterationContext`
carrying the 1-based `Iteration`, `MaxIterations`, `IsStreaming` (true only for the streamed
first call), the exact `SystemPrompt` that call will send, the run's `CancellationToken` and a
read-only live view of the conversation. Messages added through `AppendUserMessage` SHALL be
appended as `user` turns and sent with that iteration's call; a blank message SHALL be refused
with `ArgumentException`. `Stop(reason)` SHALL end the run before the model call, returning the
conversation with any messages appended in the same hook call. `Agent.CurrentSystemPrompt` SHALL
expose the `SystemPrompt` the next call will send.

#### Scenario: Hooks fire in loop order on both paths

- **Given** hooks attached and a model that requests one tool, then ends its turn
- **When** `RunAsync` runs, streamed or not
- **Then** the order is iteration 1, provider call 1, tool started, tool executed, tool
  finished, iteration 2, provider call 2, and `IsStreaming` is set only on iteration 1 of a
  streaming run

#### Scenario: Stop ends the run before the model call

- **Given** an iteration hook that calls `Stop("enough")` at iteration 2
- **When** the run reaches iteration 2
- **Then** only one provider call was made and the returned conversation ends with the
  tool-result `user` turn

#### Scenario: Stop on a cancelled token returns normally

- **Given** a cancelled token and an iteration hook that stops when cancellation is requested
- **When** `RunAsync` runs
- **Then** it returns the seeded conversation without throwing and no provider call is made

#### Scenario: An appended message reaches the provider

- **Given** an iteration hook that calls `AppendUserMessage("reflect now")` at iteration 2
- **When** the second provider call is made
- **Then** its last message is that `user` turn and its system prompt equals the context's
  `SystemPrompt` and `CurrentSystemPrompt`

#### Scenario: No hooks attached

- **Given** `Hooks = null`
- **When** a run executes one tool and ends its turn
- **Then** the loop behaves as without hooks: one tool execution, two provider calls, four turns

### Requirement: Tool-call hooks observe every tool call

The system SHALL, when `Hooks` is set, invoke `OnToolCallStartingAsync` with a `ToolCallStarted`
(iteration, tool-use id, tool name, read-only input) before resolving and executing each
requested tool, and `OnToolCallFinishedAsync` with a `ToolCallFinished` after it — carrying the
same identity and input plus `Result`, `ToolFound`, `IsError`, `Exception` and `Duration`.
`IsError` SHALL be true for an unknown tool, a result beginning `Error:` or a thrown exception;
`Result` SHALL be null only when the tool threw. Both hooks SHALL receive the run's token.

#### Scenario: Finished hook carries structured data

- **Given** a tool that returns `"done"` for input `{"path": "a.cs"}`
- **When** the call finishes
- **Then** `ToolCallFinished` holds the tool-use id, the input, `Result = "done"`,
  `ToolFound = true`, `IsError = false`, a null `Exception` and a positive `Duration`

#### Scenario: Unknown tool is an error

- **Given** a `tool_use` block naming a tool the agent does not hold
- **When** the finished hook fires
- **Then** `ToolFound = false`, `IsError = true` and `Result` begins `"Error:"`

#### Scenario: Throwing tool

- **Given** a tool whose `ExecuteAsync` throws
- **When** the finished hook fires
- **Then** `Exception` is that exception, `IsError = true`, `Result` is null, and the exception
  then propagates out of the run

### Requirement: A failing hook ends the run as AgentHookException

The system SHALL wrap any exception thrown by a hook, other than `OperationCanceledException`,
in an `AgentHookException` carrying the hook's name, the iteration and the hook's exception as
`InnerException`, and SHALL let `OperationCanceledException` propagate unwrapped. When
`OnToolCallFinishedAsync` throws while reporting a tool's exception, that tool exception SHALL
be carried as `ToolException`.

#### Scenario: Throwing iteration hook

- **Given** an iteration hook that throws `InvalidOperationException`
- **When** `RunAsync` runs
- **Then** `AgentHookException` is thrown with `HookName = "OnIterationStartingAsync"`,
  `Iteration = 1` and that exception as `InnerException`, and no provider call is made

#### Scenario: Throwing finished hook while a tool exception is in flight

- **Given** a tool that throws and a finished hook that throws `ArgumentException`
- **When** the call finishes
- **Then** `AgentHookException` is thrown with the `ArgumentException` as `InnerException` and the
  tool's exception as `ToolException`

#### Scenario: Cancellation from a hook

- **Given** an iteration hook that throws `OperationCanceledException`
- **When** `RunAsync` runs
- **Then** exactly `OperationCanceledException` propagates and no provider call is made

### Requirement: Providers are created through a registration-based factory

The system SHALL keep provider factory delegates in a case-insensitive
`ConcurrentDictionary` inside `LlmProviderFactory`, SHALL reject a null/whitespace provider
name with `ArgumentException` and a null factory with `ArgumentNullException`, SHALL let a
re-registration replace the previous delegate, and SHALL throw `ArgumentException` naming the
registered providers when `Create` is asked for an unregistered name.

#### Scenario: Case-insensitive creation

- **Given** `Register("Claude", factory)`
- **When** `Create("claude")` is called
- **Then** the registered factory is invoked with the supplied config dictionary (or `null`)

#### Scenario: Unregistered provider

- **Given** only `"openai"` registered
- **When** `Create("gemini")` is called
- **Then** `ArgumentException` is thrown with a message listing the available providers

#### Scenario: Registration overwrite

- **Given** `"openai"` registered twice with different delegates
- **When** `Create("openai")` is called
- **Then** the most recently registered delegate is used

#### Scenario: IsRegistered rejects blank names

- **Given** any registry state
- **When** `IsRegistered("  ")` is called
- **Then** it returns `false` without touching the dictionary

### Requirement: Agents are created through a registration-based factory with aliases

The system SHALL keep agent factory delegates and alias mappings in two case-insensitive
`ConcurrentDictionary` instances inside `AgentFactory` (TASK-160, SH-M156), return snapshots from
`GetRegisteredAgentTypes` / `GetRegisteredAliases`, SHALL resolve an agent type through a
single alias hop (`ResolveAgentType` returns its input when no alias matches), SHALL default
`Create`'s `agentType` to `"coding"`, SHALL substitute a fresh `AgentOptions` when none is
given, and SHALL throw `ArgumentNullException` for a null provider or `ArgumentException` naming
the registered types for an unresolvable agent type.

#### Scenario: Alias resolution

- **Given** `Register("coding", f)` and `RegisterAlias("general", "coding")`
- **When** `Create(provider, null, "general")` is called
- **Then** `f` is invoked with the provider and a default `AgentOptions`

#### Scenario: Alias chains are not followed

- **Given** `RegisterAlias("a", "b")` and `RegisterAlias("b", "coding")`
- **When** `ResolveAgentType("a")` is called
- **Then** it returns `"b"`, and `Create(provider, null, "a")` throws `ArgumentException`
  because `"b"` is not a registered factory

#### Scenario: An alias whose target is missing names both

- **Given** `RegisterAlias("docs", "writer")` with no `"writer"` factory
- **When** `Create(provider, null, "docs")` is called
- **Then** the `ArgumentException` (parameter `agentType`) names `"docs"` and the missing `"writer"` (TASK-173,
  SH-L164); a null or blank type is an `ArgumentException` too (SH-L165)

#### Scenario: Registration racing creation and enumeration

- **Given** eight threads registering types and aliases while creating agents and enumerating the registry
- **When** they run together
- **Then** nothing throws, as in `LlmProviderFactory`

### Requirement: Tools declare a name, description and JSON input schema

The system SHALL require every `Tool` to expose `Name`, `Description` and `InputSchema`, to
implement `ExecuteAsync(workingDirectory, input, cancellationToken)` returning a string, and
SHALL give each tool an optional `MessageCallback` (raised via the protected `SendMessage`) and
an optional `AgentOptions` reference used for path allow-listing and interactivity. Every built-in tool SHALL report a failure as a string beginning `"Error:"`, the prefix the agent counts as a failed
tool execution (TASK-160, SH-M153: eight tools' exception paths began `"Error reading file:"` and the like, so a
batch in which every tool threw counted as success).

#### Scenario: Tool result string is the only channel back to the model

- **Given** any built-in tool
- **When** execution succeeds or fails
- **Then** the outcome is expressed entirely as the returned string; failures are conveyed as
  strings beginning with `"Error"` rather than as thrown exceptions

### Requirement: File tools confine paths to the workspace and allow-list

The system SHALL check every file path against `PathHelper.IsPathSafe(fullPath,
workingDirectory, Options?.AllowedExternalPaths)` before touching the filesystem in
`read_file`, `write_file`, `edit_file`, `append_to_file`, `list_files` and `search_code` (TASK-160, SH-M167: it used the
two-argument overload and ignored `AllowedExternalPaths`), and SHALL return an `"Error: Access denied. …"` string when
the check fails.

#### Scenario: Traversal outside the workspace is denied

- **Given** `workingDirectory = "C:\\repo"` and `file_path = "..\\..\\secrets.txt"`
- **When** `read_file` executes
- **Then** the access-denied error string is returned and no file is read

#### Scenario: Allow-listed external path is readable

- **Given** `Options.AllowedExternalPaths = ["C:\\shared"]` and a path resolving under it
- **When** `read_file` executes
- **Then** the safety check passes and the file content is returned

#### Scenario: search_code reaches an allow-listed path

- **Given** the same allow-listed `C:\shared` and `directory` pointing at it
- **When** `search_code` executes
- **Then** the directory is searched, as every other file tool would read it

#### Scenario: A single leading slash is workspace-relative for every path-taking file tool

- **Given** `file_path = "/etc/passwd"` (or `directory = "/etc"` for `list_files`)
- **When** `read_file`, `write_file`, `edit_file`, `append_to_file` or `list_files` runs
- **Then** the leading `/` is stripped and the path is resolved under the workspace (`etc/passwd`); a `//` prefix or a
  drive-rooted path stays absolute and must pass the allow-list (TASK-173, SH-L171: the three write tools used a bare
  `Path.Combine`, so a rooted path discarded the workspace and was denied). `search_code` still joins `directory` with
  a bare `Path.Combine`

### Requirement: read_file returns raw content or an error string

The system SHALL require a non-empty `file_path`, normalise the workspace with
`Path.GetFullPath`, strip a single leading `/`, resolve the full path, apply the safety check and
return the file's entire text; any exception SHALL be returned as
`"Error: reading file failed: {message}"`.

#### Scenario: Missing file

- **Given** a safe path that does not exist
- **When** `read_file` executes
- **Then** the `FileNotFoundException` is caught and `"Error: reading file failed: …"` is returned

#### Scenario: Missing file_path key

- **Given** an input dictionary without `file_path`
- **When** `input["file_path"]` throws `KeyNotFoundException`
- **Then** the catch block returns `"Error: reading file failed: …"`

### Requirement: list_files enumerates workspace-relative paths

The system SHALL treat an absent, empty, `"."` or `"./"` `directory` as the workspace root,
enumerate files with `SearchOption.AllDirectories` when `recursive` parses to `true` and
`TopDirectoryOnly` otherwise, project results to workspace-relative paths sorted
lexicographically, and render them as a header line, the indented paths and a
`"Total: {n} file(s)"` footer.

#### Scenario: Empty directory

- **Given** an existing but empty target directory
- **When** `list_files` executes
- **Then** `"No files found in: {dir}"` is returned instead of a listing

#### Scenario: Non-existent directory

- **Given** `directory = "nope"`
- **When** the safety check passes but the directory does not exist
- **Then** `"Error: Directory not found: nope"` is returned

#### Scenario: recursive defaults to false

- **Given** no `recursive` key, or a value that does not parse as a boolean
- **When** `list_files` executes
- **Then** only the top-level directory is enumerated

### Requirement: Every file tool resolves a path the same way

The system SHALL resolve a model-supplied path through `WorkspacePath.Resolve` in `read_file`, `write_file`,
`edit_file` and `append_to_file` (TASK-173, SH-L171): a single leading `/` is workspace-relative, and the result is
a full path checked against the workspace and the allow-list. `write_file` and the other two used a bare
`Path.Combine`, so `/src/x.cs` was read but denied for writing.

#### Scenario: Read and write agree on a slash-prefixed path

- **Given** `file_path = "/src/x.cs"` in workspace `C:\repo`
- **When** `write_file` creates it and `read_file` then reads it
- **Then** both resolve `C:\repo\src\x.cs`, the write returns `"OK"` and the read returns the written content

### Requirement: write_file refuses to clobber by default

The system SHALL require `file_path`, default `content` to the empty string when the key is
absent, default `create_directories` and `check_exists` to `true` when absent or unparseable,
return `"Warning: File '{path}' already exists. Use edit_file to modify it, or call write_file
with check_exists=false to overwrite."` when the target exists and `check_exists` holds, create
missing parent directories when `create_directories` holds (otherwise returning
`"Error: Directory does not exist: {dir}"`), and return `"OK"` on success.

#### Scenario: Existing file is not overwritten

- **Given** an existing `README.md` and no `check_exists` key
- **When** `write_file` executes
- **Then** the warning string is returned, the file is untouched, and because the string does not
  begin with `"Error:"` the agent does not count it as a failed tool execution

#### Scenario: Explicit overwrite

- **Given** `check_exists = false` for an existing file
- **When** `write_file` executes
- **Then** the file is replaced and `"OK"` is returned

#### Scenario: Missing parent directory with creation disabled

- **Given** `create_directories = false` and a path under a non-existent folder
- **When** `write_file` executes
- **Then** `"Error: Directory does not exist: {dir}"` is returned and nothing is written

### Requirement: edit_file performs a single unambiguous replacement

The system SHALL require `file_path` and a non-empty `old_text`, require the file to exist,
return the full file content (truncated to 8000 characters with a
`"... (truncated, {n} chars total)"` marker) inside an error message when `old_text` is not
found, refuse the edit when `old_text` occurs more than once, and otherwise replace the single
occurrence and return `"OK"`.

#### Scenario: old_text not present

- **Given** a file whose text does not contain `old_text`
- **When** `edit_file` executes
- **Then** the returned string starts with `"Error: old_text not found in file."` and embeds the
  file content in a fenced block so the model can retry with exact text

#### Scenario: Ambiguous old_text

- **Given** `old_text` occurring three times
- **When** `edit_file` executes
- **Then** `"Error: old_text appears 3 times. Provide a more specific text block."` is returned
  and the file is unchanged

#### Scenario: Empty old_text

- **Given** `old_text = ""`
- **When** the guard throws `ArgumentException`
- **Then** `"Error: editing file failed: old_text is required"` is returned

#### Scenario: Missing file

- **Given** a safe path that does not exist
- **When** `edit_file` executes
- **Then** `"Error: File does not exist: {path}"` is returned

### Requirement: append_to_file appends and may create the file

The system SHALL append `content` to the target with `File.AppendAllText` — creating the file
when absent — after requiring `file_path`, defaulting `content` to the empty string, defaulting
`create_directories` to `true`, and applying the path safety check; it SHALL return `"OK"` on
success and `"Error: appending to file failed: {message}"` on any exception.

#### Scenario: Append to a non-existent file

- **Given** a safe path with no existing file and `create_directories` unset
- **When** `append_to_file` executes
- **Then** the parent directory is created if needed, the file is created with the content, and
  `"OK"` is returned

### Requirement: search_code scans files line by line

The system SHALL require a non-empty `query`, default `pattern` to `"*"`, default `recursive`
to `true` (any unparseable value also yielding `true`), default `regex` and `case_sensitive` to
`false`, skip files with extensions `.png .jpg .jpeg .gif .bmp .pdf .zip`, skip files it cannot
read, and return one `"{relativePath}:{lineNumber}: {line}"` entry per match joined by
`Environment.NewLine`. A regex query SHALL be compiled with `MatchTimeout` (2 s per line), a timed-out match SHALL
return an `"Error: the regex timed out …"` string, and the caller's cancellation SHALL be observed between files
(TASK-160, SH-M168: a catastrophic model-supplied pattern pinned a CPU and ignored cancellation).

#### Scenario: Case-insensitive substring search

- **Given** `query = "todo"` with `case_sensitive` unset
- **When** `search_code` runs
- **Then** lines containing `"TODO"` match via `StringComparison.InvariantCultureIgnoreCase`

#### Scenario: No matches

- **Given** a query occurring nowhere
- **When** `search_code` completes
- **Then** `No matches for '{query}' in N file(s) searched ({pattern}).` is returned (TASK-173, SH-L173: the empty
  string, which also meant "nothing was searched"); an inaccessible subdirectory is skipped, not fatal (SH-L172)

#### Scenario: Invalid regular expression

- **Given** `regex = true` and `query = "["`
- **When** the `Regex` constructor throws
- **Then** `"Error: searching code failed: …"` is returned

#### Scenario: Unreadable file is skipped

- **Given** a locked or binary-but-unlisted file inside the search set
- **When** `File.ReadAllLines` throws
- **Then** that file is skipped and the scan continues over the rest

### Requirement: run_command executes a process and returns its combined output

The system SHALL require a non-empty `command`, an existing `workingDirectory` and a positive
`timeout_seconds` (default 120), and run the process through `Birko.Helpers.ProcessRunner` (TASK-534, the one
runner): both standard streams drained concurrently from the start, the process tree killed on timeout. No shell
runs the command (the schema says so: builtins, pipes and redirection need an explicit `cmd /c` or `sh -c`, TASK-173
SH-L168). On exit code 0 it SHALL return stdout, a newline and stderr when both are non-empty, stderr alone when
stdout is empty, or stdout alone otherwise, and SHALL let the caller's cancellation propagate as
`OperationCanceledException`.

#### Scenario: Successful command

- **Given** `command = "git"`, `arguments = "status"` in a valid working directory
- **When** the process exits with empty stderr
- **Then** the captured stdout is returned verbatim

#### Scenario: Command writing to stderr

- **Given** a process producing both stdout and stderr
- **When** it exits with code 0
- **Then** the result is stdout, a newline, then stderr — with no indication of the exit code

#### Scenario: Timeout kills the process tree

- **Given** `timeout_seconds = 1` and a process that runs longer
- **When** the timeout elapses
- **Then** `ProcessRunner` kills the tree and reports `TimedOut`, and `"Error: Process timed out"` is returned,
  followed by a newline and whatever the process printed to stdout and stderr before it was killed (TASK-173, SH-L166:
  that output was thrown away)

#### Scenario: Caller cancellation propagates

- **Given** the agent's `cancellationToken` is cancelled while the process runs
- **When** `ProcessRunner` observes it
- **Then** the process tree is killed and `OperationCanceledException` propagates (before TASK-534 the tool returned
  `"Error: Process timed out"` and swallowed the cancellation)

#### Scenario: A non-zero exit code is an error

- **Given** a command exiting with code 3 and no output
- **When** `run_command` returns
- **Then** `"Error: the command exited with code 3."` is returned (followed by any output), so the agent counts the
  failure (TASK-160, SH-M169: the empty string was returned)

#### Scenario: Invalid working directory

- **Given** a `workingDirectory` that does not exist
- **When** `run_command` executes
- **Then** the `ArgumentException` guard produces `"Error: running command failed: Invalid working directory"`

### Requirement: display_text surfaces text through the message callback

The system SHALL require a non-empty `text`, prepend `title` followed by a newline when a
non-blank title is supplied, emit the composed string as a `"display"` message and return
`"Text displayed successfully"`, and SHALL return an `Error:` string when no message callback is attached, since the
text then goes nowhere (TASK-173, SH-L170).

#### Scenario: Title and body

- **Given** `title = "Summary"` and `text = "3 files changed"`
- **When** `display_text` executes
- **Then** the `"display"` message body is `"Summary\n3 files changed"`

#### Scenario: Blank text

- **Given** `text = "   "`
- **When** `display_text` executes
- **Then** `"Error: text parameter is required"` is returned and no message is emitted

#### Scenario: No callback attached

- **Given** a `display_text` tool whose `MessageCallback` is null
- **When** it executes with non-blank text
- **Then** `"Error: no output is attached, so the text was not displayed."` is returned, which the agent counts as a
  failed tool execution

### Requirement: ask_user honours non-interactive mode and a prompt timeout

The system SHALL require a non-empty `question`; when `Options` is non-null and
`Options.Interactive` is false it SHALL return `Options.DefaultPromptResponse` (after emitting an
`"info"` message) if that response is non-empty, and otherwise
`"Error: Cannot prompt for user input in non-interactive mode."`. In interactive mode with a
`PromptCallback` it SHALL emit a `"prompt"` message with the composed question and race the
callback against `Task.Delay(Options.PromptTimeout ?? 300 seconds)`. Without a `PromptCallback`
it SHALL emit `"prompt_console"` and return
`"Error: Console input not available in library mode. Use PromptCallback for interactive prompts."`.

#### Scenario: Non-interactive with a canned answer

- **Given** `Interactive = false` and `DefaultPromptResponse = "yes"`
- **When** `ask_user` executes
- **Then** an `"info"` message `"[Non-Interactive] Auto-responding to: {question}"` is emitted and
  `"yes"` is returned

#### Scenario: Non-interactive without a canned answer

- **Given** `Interactive = false` and a null `DefaultPromptResponse`
- **When** `ask_user` executes
- **Then** `"Error: Cannot prompt for user input in non-interactive mode."` is returned

#### Scenario: Prompt times out

- **Given** a `PromptCallback` that never completes and `PromptTimeout = 30`
- **When** the delay wins the race
- **Then** `"Error: Prompt timed out after 30 seconds."` is returned and the abandoned prompt task's fault, if any,
  is observed (TASK-160, SH-M170)

#### Scenario: Cancellation propagates

- **Given** a `cancellationToken` cancelled while the prompt is pending
- **When** the cancelled delay completes first
- **Then** `OperationCanceledException` propagates (TASK-160, SH-M170: the prompt-timeout error was returned)

#### Scenario: Context is prepended to the question

- **Given** a non-blank `context`
- **When** the prompt is emitted
- **Then** the message body is `"Context: {context}\n\nQuestion: {question}"` while the callback
  still receives question and context as separate arguments

### Requirement: Built-in providers register once under fixed names and defaults

The system SHALL register, in `ProviderRegistration.RegisterAll()`, exactly once per process (double-checked lock,
safe to call concurrently), the providers `openai`, `azureopenai`, `claude`, `gemini`, `ollama`, `llamacpp`, `vllm`,
`sglang`, `githubcopilot`, `zai`, `mistral`, `deepseek`, `groq`, `lmstudio` and `openrouter`, plus `zhipu` and
`zhipuai` as delegates that create `zai`. Each factory SHALL read its config keys case-sensitively as given
(`apiKey`, `model`, `baseUrl`, `endpoint`, `deployment`, `clientId`, `deepThinking`, `useCodingEndpoint`) and SHALL
treat an absent, null-config or blank value as the provider's default (`openai` → `gpt-4o`, `claude` →
`claude-sonnet-4-6`, `gemini` → `gemini-2.0-flash-exp`, `ollama` → `llama3.2` at `http://localhost:11434`, `zai` →
`ZAiProvider.DefaultModel`, …). `deepThinking` and `useCodingEndpoint` SHALL be true only for a case-insensitive
`"true"`.

#### Scenario: Blank config value falls back to the default

- **Given** `RegisterAll()` and the config `{"apiKey": "k", "model": "  "}`
- **When** `LlmProviderFactory.Create("claude", config)` is called
- **Then** a `ClaudeProvider` for model `claude-sonnet-4-6` at `https://api.anthropic.com/v1/messages` is returned

#### Scenario: A Zhipu alias creates Z.AI

- **Given** `RegisterAll()`
- **When** `LlmProviderFactory.Create("zhipuai", config)` is called
- **Then** the `zai` factory runs with the same config and a `ZAiProvider` is returned

#### Scenario: Repeated registration is a no-op

- **Given** `RegisterAll()` has run and a consumer re-registered `"openai"` with its own delegate
- **When** `RegisterAll()` is called again
- **Then** nothing is re-registered and the consumer's delegate stays in place

### Requirement: Every HTTP provider follows one request/response shape

The system SHALL have every concrete HTTP provider (OpenAI, Azure OpenAI, Claude, Gemini, Ollama, Z.AI, GitHub
Copilot and `OpenAiCompatibleProviderBase`) answer `SendMessageAsync` by: returning `NotConfigured()` when its
`IsConfigured()` fails, sending through `SendWithRetryAsync`, answering a null response with
`LlmResponse.Error("{Name}: No response received")` (an `Unknown` failure), answering a non-success status with
`"{Name} API Error ({status}): {detail or raw body}"` emitted as `"error"` and classified by `ClassifyResponse`, and
answering any other exception except the caller's cancellation with `"Error calling {Name} API: {message}"` classified
by `ClassifyException`. A non-success status therefore emits two `"error"` messages: one from the retry loop and one
from the provider. Its streaming path SHALL return an `LlmStreamingResponse` with `Error = "Not configured"` (and no
`FinalResponse`) when not configured, a `StreamingFailure` when the connection fails, and otherwise a response whose
`Resource` is the live HTTP response and whose `GetStreamAsync` parses the body lazily. `IsConfigured()` SHALL check:
API key and model (OpenAI, Claude, Gemini), API key and deployment (Azure — the endpoint is not checked), API key
(Z.AI), model (Ollama, Copilot streaming), base URL (OpenAI-compatible — an empty API key is never refused).

#### Scenario: A missing key never reaches the network

- **Given** an `OpenAiProvider` constructed with an empty API key
- **When** `SendMessageAsync` is called
- **Then** `StopReason = "NotConfigured"` and `Failure.Kind = NotConfigured` are returned and no request is sent

#### Scenario: DeepSeek without a key is sent anyway

- **Given** a `DeepSeekProvider` constructed with an empty API key
- **When** `SendMessageAsync` is called
- **Then** the request is sent without an `Authorization` header and the provider's 401 comes back as an
  `Unauthorized` failure

#### Scenario: Not-configured streaming has no final response

- **Given** a `ClaudeProvider` with no API key
- **When** `SendMessageStreamingAsync` is awaited
- **Then** `Error = "Not configured"`, `FinalResponse` is null and `GetStreamAsync` faults with
  `InvalidOperationException`; the agent turns the error into `InvalidOperationException("Streaming failed: Not configured")`

### Requirement: OpenAI-compatible providers share a base with its own parser

The system SHALL build llama.cpp, vLLM, SGLang, Mistral, DeepSeek, Groq, LM Studio and OpenRouter on
`OpenAiCompatibleProviderBase`, whose `Name` is `"{ProviderName} ({Model})"`, whose `HttpClient` timeout is 5 minutes,
which sends `Authorization: Bearer {apiKey}` only when a key is given, and which posts to `ChatCompletionsUrl`: the
base URL as-is when it already ends in `/chat/completions` (case-insensitive), otherwise `{baseUrl}/v1/chat/completions`
(CR-H004). Payloads SHALL carry `model`, the OpenAI-style messages and tools, `temperature` (default 0.7) and
`max_tokens` (default 8192; llama.cpp `-1`). Its sync parser `ParseResponse` SHALL NOT use `ParseOpenAiStyleResponse`:
it reads `tool_calls` (an array with at least one entry → `"tool_use"`, generating a `Guid` id when a call has none)
or else the text (`"end_turn"`, an empty text adds no block), ignores `finish_reason` and `usage`, and drops a text
block that accompanies tool calls. Its streaming path SHALL use `ParseOpenAiStreamChunksWithToolCapture`.

#### Scenario: Bare host gets the path appended

- **Given** a `VllmProvider` with base URL `http://localhost:8000/`
- **When** a request is sent
- **Then** it goes to `http://localhost:8000/v1/chat/completions`, while a `MistralProvider` keeps
  `https://api.mistral.ai/v1/chat/completions` unchanged

#### Scenario: A truncated sync completion reads as finished

- **Given** a compatible provider answering `finish_reason = "length"` with text only
- **When** `SendMessageAsync` parses it
- **Then** `StopReason` is `"end_turn"` (not `"max_tokens"`) and `Usage` is null, so a `TrackedLlmProvider` records no usage

#### Scenario: A null error field fails the call

- **Given** a compatible server that sends `"error": null` beside a valid completion
- **When** `ParseResponse` reads it
- **Then** `TryGetProperty("message")` on the null value throws, and the call becomes
  `"Error calling {ProviderName} API: …"` with `Failure.Kind = InvalidResponse`

#### Scenario: Mistral gets a placeholder between a tool result and a user turn

- **Given** a conversation ending in a tool-result turn followed by a hook's appended user message
- **When** `MistralProvider` builds the request
- **Then** an `{role="assistant", content=" "}` message is inserted between the `tool` and `user` entries in the
  request only; the agent's conversation is unchanged (TASK-546)

### Requirement: Claude speaks the Messages API natively

The system SHALL send Claude requests to the configured URL with headers `x-api-key` and
`anthropic-version: 2023-06-01`, a body of `model`, `max_tokens = 8192`, `system`, `messages` and `tools`
(`{name, description, input_schema}`), mapping a `ContentBlock` list to `{type, text}` / `{type, id, name, input}`
dictionaries and passing any other content (a string, the agent's anonymous tool_result objects) through as-is. The
sync parser SHALL take `StopReason` verbatim from `stop_reason`, keep text and tool_use blocks in order (tool input
deserialised whole, CR-L011), read `usage.input_tokens` / `output_tokens` and `model`, classify a body `error` object
by its `type` (`rate_limit_error` RateLimited, `overloaded_error`/`api_error` ServerError, `timeout_error` Timeout,
`invalid_request_error`/`request_too_large` BadRequest, `authentication_error` Unauthorized, `billing_error` Billing,
`permission_error` Forbidden, `not_found_error` NotFound, else Unknown) and turn any parse exception into
`"Error parsing Claude response: …"` (`InvalidResponse`). The streaming parser SHALL yield `text_delta` text,
assemble each `tool_use` block from `input_json_delta` fragments at `content_block_stop` (falling back to
`{"_raw": …}` when they do not parse), take `stop_reason` from `message_delta` and token counts from `message_start` /
`message_delta`, and default the stop reason to `"end_turn"` when none arrived.

#### Scenario: Claude stop reasons pass through

- **Given** a Claude body with `stop_reason = "max_tokens"` and a `tool_use` block
- **When** it is parsed
- **Then** `StopReason` is `"max_tokens"`, the tool_use block is still in `Content`, and the agent ends the run as
  `Truncated` without executing it

#### Scenario: A stream that ends without a message_delta reads as finished

- **Given** a Claude stream that delivers text deltas and then an `error` event (or simply ends) with no `message_delta`
- **When** the stream completes
- **Then** the `error` event is ignored and `FinalResponse.StopReason` is `"end_turn"`

#### Scenario: A streamed truncated tool call is still returned

- **Given** a Claude stream whose tool input fragments do not form valid JSON and whose `stop_reason` is `"max_tokens"`
- **When** the stream completes
- **Then** the tool_use block carries `{"_raw": …}` and `StopReason` is `"max_tokens"`

### Requirement: Gemini translates the conversation into alternating contents

The system SHALL send Gemini requests to `{baseUrl}{model}:generateContent?key={apiKey}` (streaming:
`:streamGenerateContent?alt=sse&key=…`) with `contents`, `systemInstruction` and `generationConfig.maxOutputTokens =
8192`, adding `tools[].functionDeclarations` only when tools exist. A tool call SHALL become a `functionCall` part and
a tool result a `functionResponse` part whose `name` is the function name looked up by the tool-use id in the
conversation's assistant turns, or the id itself when no such call is present (TASK-554). Consecutive messages mapped
to the same role (`assistant` → `model`, anything else → `user`) SHALL be merged into one content in the request only
(TASK-546), and a message contributing no part SHALL be skipped. Gemini supplies no call ids, so each parsed
`functionCall` SHALL get a new `Guid` id. A `finishReason` of `SAFETY` or `RECITATION` SHALL be an error
(`"Gemini blocked response: {reason}"`, `Unknown` failure) in the sync parser and a bare `StopReason = "error"` in the
streaming parser. The sync stop reason SHALL be set per part in order — `"end_turn"` for a text part, `"tool_use"`
for a `functionCall` part — so the last part decides it; the streaming stop reason SHALL be `"tool_use"` whenever any
call was captured.

#### Scenario: A tool result names the function, not the id

- **Given** an assistant turn with tool_use `{id: "g1", name: "read_file"}` and a tool_result for `"g1"`
- **When** the Gemini payload is built
- **Then** the `functionResponse.name` is `"read_file"`

#### Scenario: Text after a function call hides the call

- **Given** a sync Gemini response whose parts are `[functionCall read_file, text "done"]`
- **When** it is parsed
- **Then** `StopReason` is `"end_turn"`, so the agent ends the run as `Completed` and never executes the call

#### Scenario: A blocked stream fails with no message

- **Given** a Gemini stream ending with `finishReason = "SAFETY"` and no function call
- **When** the agent handles it
- **Then** `StopReason = "error"` with no `ErrorMessage` or `Failure`, so the run ends `LlmFailed` with
  `Detail = "Unknown LLM error"` and a null `Failure`

### Requirement: Ollama, Z.AI and GitHub Copilot add provider-specific behaviour

The system SHALL post Ollama requests to `{baseUrl}/api/chat` with `stream = false` and `num_predict = -1`, a 5-minute
timeout and `Name = "Ollama ({model})"`, parse `message.tool_calls` with a new `Guid` id per call and
`function.arguments` read as a JSON string, and otherwise `message.content` as `"end_turn"`. Z.AI SHALL post to
`{baseUrl}/chat/completions` where the base URL is the given one (trailing `/` trimmed), else
`https://api.z.ai/api/coding/paas/v4` when `useCodingEndpoint`, else `https://api.z.ai/api/paas/v4`; add
`thinking: {type: "enabled"}` when deep thinking is on; send `max_tokens` per model (131 072 for GLM-5.1/5.1 Turbo/5.3,
128 000 for GLM-5/5 Turbo, 65 536 for 5.3 Flash, 8192 for GLM-4.7/4.8, 4096 otherwise); emit a `"thinking"` message
from the first of `thinking_content`, `reasoning_content`, `thinking` present on a text turn; map `finish_reason =
"length"` to `"max_tokens"`; and replace unparseable tool arguments by an empty input after an `"error"` message.
GitHub Copilot SHALL obtain an OAuth token through its `IOAuthClient` (re-fetched when within 60 s of expiry), answer
a failed authentication with `NotConfigured()` (sync) or `Error = "Authentication failed"` (streaming), send
`Editor-Version` / `Editor-Plugin-Version` headers on every request (CR-H005), and on a 401 refresh the token once and
retry — in streaming by reading the `Unauthorized` classification from `ConnectStreamingAsync` (TASK-531).

#### Scenario: Ollama's native arguments object fails the parse

- **Given** an Ollama `/api/chat` response whose `function.arguments` is a JSON object (Ollama's native shape)
- **When** `ParseResponse` calls `GetString()` on it
- **Then** `InvalidOperationException` is thrown and the call becomes `"Error calling Ollama API: …"` with
  `Failure.Kind = InvalidResponse`

#### Scenario: Copilot refreshes a rejected token once

- **Given** a Copilot request answered 401, then 200 after `RefreshTokenAsync`
- **When** `SendMessageAsync` or `SendMessageStreamingAsync` runs
- **Then** the token is refreshed once and the second attempt's response is returned

#### Scenario: Z.AI length limit

- **Given** a Z.AI completion with `finish_reason = "length"`
- **When** it is parsed
- **Then** `StopReason` is `"max_tokens"` even when tool calls were parsed

### Requirement: The built-in agent catalogue registers once with aliases

The system SHALL register, in `AgentRegistration.RegisterAll()`, exactly once per process and after
`ProviderRegistration.RegisterAll()`, the agent types `coding`, `csharp`, `cpp`, `assembler`, `javascript` and
`typescript` (both `JavaScriptTypeScriptCodingAgent`), `css`, `html`, `react`, `angular`, `php`, `python`,
`documentation`, `debug`, `refactor`, `test`, `diagramming`, `media`, `image`, `svg` and `bitmap`, and the aliases
`general` → `coding`, `docs` → `documentation`, `debugging` → `debug`, `refactoring` → `refactor`, `testing` →
`test`, `diagram` → `diagramming`. Every catalogue agent SHALL be prompt-only: it overrides `SystemPrompt` (and the
four task agents `GetDepthGuidance`) and keeps the nine default tools. `IsCodingAgent(type)` SHALL be true,
case-insensitively, for `coding`, the eleven language types, `debug`, `refactor`, `test` and `svg`.
`AgentRegistration.Create(provider, options, config, agentType)` SHALL register both catalogues, inject
`useCodingEndpoint = "true"` into the config for `zai` / `zhipu` / `zhipuai` when the resolved type is a coding agent
and the key is absent, then create the provider through `LlmProviderFactory` and the agent through `AgentFactory`.

#### Scenario: Z.AI coding endpoint is chosen by agent type

- **Given** `AgentRegistration.Create("zai", null, new() { ["apiKey"] = "k" }, "csharp")`
- **When** the provider is created
- **Then** the config carries `useCodingEndpoint = "true"` and the provider posts to the coding endpoint; for
  `agentType = "documentation"` no flag is added

#### Scenario: The caller's config is mutated

- **Given** a config dictionary passed to `AgentRegistration.Create` for `zai` with a coding agent type
- **When** the call returns
- **Then** the caller's own dictionary now contains `useCodingEndpoint = "true"`

#### Scenario: Alias resolves to a catalogue agent

- **Given** `RegisterAll()`
- **When** `AgentFactory.Create(provider, null, "docs")` is called
- **Then** a `DocumentationAgent` is returned

### Requirement: Orchestrator agents extract text and JSON from model output

The system SHALL give `OrchestratorAgent` an opt-in `GetOrchestratorGuidance()` text, a three-band depth guidance of
its own, and two static helpers. `ExtractTextFromContent(content)` SHALL return a string as-is; a single
`ContentBlock`'s `Text` whatever its `Type`; the newline-join of non-empty `text` blocks of a block list, of a
`JsonElement` array or of an object list (a `ContentBlock`, a string, or any object with `Type`/`type` and
`Text`/`text` properties); a `JsonElement` string's value; the empty string for a `List<>` or enumerable it could not
read; and `ToString()` of anything else. `ExtractJson(content)` SHALL throw `InvalidOperationException` for blank
content or content starting with `Error:`, and otherwise return the first brace-balanced object — from the start when
the trimmed content starts with `{`, else from a fenced (```` ```json ```` or ```` ``` ````) block that starts with `{`,
else from the first `{` anywhere — honouring string literals and escapes, and SHALL throw when braces never balance or
no `{` exists (naming the first 100 characters).

#### Scenario: JSON inside a fenced block

- **Given** a reply of prose followed by a ```` ```json ```` block holding `{"a": "}"}`
- **When** `ExtractJson` runs
- **Then** it returns `{"a": "}"}`, the brace inside the string literal not closing the object

#### Scenario: An error reply is not parsed

- **Given** the content `Error: LLM request failed: 429`
- **When** `ExtractJson` runs
- **Then** `InvalidOperationException("LLM request failed: Error: …")` is thrown

#### Scenario: A tool_use block is read as text

- **Given** a single `ContentBlock { Type = "tool_use", Text = "x" }`
- **When** `ExtractTextFromContent` runs
- **Then** it returns `"x"`, unlike `MessageText.From`, which returns the empty string for a non-text block

### Requirement: A tracked provider waits for rate limits, refuses over budget and records usage

The system SHALL let `TrackedLlmProvider` decorate any `ILlmProvider`, forwarding `Name` and `MessageCallback`, and on
each call (sync or streaming): wait for a rate-limit slot by re-checking `CanMakeRequest(Name)` up to
`MaxRateLimitWaitAttempts` (10) times, each wait being the limiter's retry-after (else 1 s) capped at
`MaxSingleRateLimitWait` (60 s), and refuse with `LlmResponse.Error("Rate limit for {Name} could not be satisfied after
waiting.")` when the final re-check fails; then refuse with `LlmResponse.Error("Budget exceeded ({type}): ${spend:F2} /
${limit:F2}")` when `CheckBudgetAsync(ProjectId)` reports the budget exceeded; then call the inner provider. Both
refusals carry an `Unknown` (permanent) failure, and a budget warning does not refuse; a streaming refusal returns an
empty stream with `Error` and `FinalResponse` set. Usage SHALL be recorded after a sync response, and after a stream has
been fully enumerated, only when the response's `Usage` has a non-zero token count: the limiter's
`RecordRequest(Name, TotalTokens)` and the cost tracker's `RecordUsageAsync` with `ProjectId`, `TaskId`, `AgentType`
and `CallerContext`, without the caller's token, logging rather than throwing a recording failure. The streaming
wrapper SHALL keep the inner response's `Resource`. The limiter and the pricing are keyed by the provider's `Name`
(e.g. `"Claude"`, `"Ollama (llama3.2)"`).

#### Scenario: A response without usage is never counted

- **Given** a rate limit of 1 request per minute and an inner provider whose responses carry no `Usage`
- **When** three calls are made within a minute
- **Then** all three proceed, because a request is recorded against the limiter only together with its tokens

#### Scenario: Budget exceeded

- **Given** a daily budget of $5 and $5.20 already recorded today
- **When** `SendMessageAsync` is called
- **Then** `StopReason = "error"` with `"Budget exceeded (daily): $5.20 / $5.00"` is returned and the inner provider is
  not called

#### Scenario: Cancellation while waiting for a slot

- **Given** a blocked rate limit and a token cancelled during the wait
- **When** the wait's `Task.Delay` observes it
- **Then** `TaskCanceledException` propagates

### Requirement: Rate limits are counted in fixed per-provider windows

The system SHALL key `ProviderRateLimiter` limits and windows by the lower-cased provider name (a later config entry
for the same name wins, CR-L013), allow every request when disabled or when no limit exists for the provider, and keep
one minute window (shared by request and token counts) and one day window per provider. A window SHALL reset entirely
once its length has elapsed since its start — a fixed window, although the type describes itself as sliding.
`CanMakeRequest` SHALL refuse when the minute request count, minute token count, day request count or day token count
has reached a positive limit. `GetRetryAfter` SHALL return the time until the latest blocking window resets, or null
when nothing blocks, the limiter is disabled or the provider has no limit. `GetAllStatuses` SHALL report counts and
limits for every configured provider.

#### Scenario: Case-insensitive provider names

- **Given** a limit configured for `"OpenAI"` of 2 requests per minute
- **When** `RecordRequest("openai")` is called twice
- **Then** `CanMakeRequest("OPENAI")` is false and `GetRetryAfter("OpenAI")` is the time left in the minute window

#### Scenario: A burst straddling a window boundary

- **Given** a limit of 10 requests per minute and 10 requests recorded in the window's last second
- **When** the window's minute elapses
- **Then** the counts reset and 10 more requests are allowed immediately

### Requirement: A circuit breaker opens per provider and can persist its state

The system SHALL track circuits by lower-cased provider name, ignoring a blank name (`CanRetry` true, `GetState`
`Closed`). `RecordFailure` SHALL increment the consecutive failures, stamp the last failure, open a closed circuit once
failures reach the threshold (default 3) and re-open a half-open one. `RecordSuccess` SHALL close the circuit and zero
its failures. `CanRetry` SHALL move an open circuit to half-open once the open duration (default 10 min) has elapsed,
zero a closed circuit's failures once the last failure is older than the reset window (default 5 min), and return
whether the circuit is not open — so every caller passes while half-open. Every `RecordFailure`, `RecordSuccess`,
`Reset` and `ResetAll` SHALL persist a snapshot to the optional `ICircuitBreakerStore`, chained per provider so saves
apply in the order the changes happened (CR-M013), logging rather than throwing a failed save (TASK-530: resets used to
stay in memory, so a reload brought a reset circuit back open); `CanRetry`'s transitions are not persisted.
`LoadPersistedStateAsync` SHALL restore every stored circuit, and `FlushPersistenceAsync` SHALL await pending saves. The
breaker is standalone: `TrackedLlmProvider` does not consult it.

#### Scenario: Threshold opens the circuit

- **Given** a breaker with threshold 3
- **When** `RecordFailure("Claude")` is called three times
- **Then** `GetState("claude")` is `Open` and `CanRetry("CLAUDE")` is false until the open duration elapses

#### Scenario: A reset survives a restart

- **Given** a breaker with a store, an open circuit for `"openai"`, then `Reset("openai")` and `FlushPersistenceAsync()`
- **When** a new breaker over the same store calls `LoadPersistedStateAsync`
- **Then** the circuit is `Closed` with zero failures

### Requirement: Cost tracking prices usage and checks budgets in a fixed order

The system SHALL price usage, in `CostTrackingService.CalculateCost`, by the pricing entry for `"{provider}:{model}"`
(case-insensitive), else `"{provider}:*"`, else 0, as `prompt / 1e6 × input price + completion / 1e6 × output price`.
`RecordUsageAsync` SHALL do nothing when disabled and otherwise persist a `UsageRecordEntity` with the computed cost
when a repository exists. `CheckBudgetAsync(projectId)` SHALL report within budget with type `"none"` when disabled
(CR-L012) or without a repository, and otherwise check, in order, the daily budget (UTC day), the monthly budget (UTC
month) and — when a project id is given — the project's all-time spend, each only when positive; the first budget that
is exceeded (spend ≥ limit) or at warning (spend ≥ limit × `WarningThresholdPercent` / 100, default 80) SHALL be
returned at once, so a warning on an earlier budget stops the later ones from being checked.

#### Scenario: Wildcard pricing

- **Given** pricing `{Provider: "Claude", Model: "*", Input: 3, Output: 15}`
- **When** `CalculateCost("claude", "claude-sonnet-4-6", 1_000_000, 100_000)` is called
- **Then** it returns 4.5

#### Scenario: A daily warning masks an exceeded monthly budget

- **Given** a daily budget of $10 with $9 spent today and a monthly budget of $100 with $120 spent this month
- **When** `CheckBudgetAsync` runs
- **Then** it returns `IsWithinBudget = true`, `IsWarning = true`, type `"daily"`, and a tracked call is not refused

### Requirement: Resilience state persists over any bulk store

The system SHALL implement `ICircuitBreakerStore` as `StoreCircuitBreakerStore` over an
`IAsyncBulkStore<CircuitBreakerStateModel>` (TASK-530): one row per provider whose `Guid` is a name-based (version 3,
MD5) Guid of `"Birko.AI.Resilience/circuit/" + provider`, saved by upsert (`SaveAsync`); a null state is an
`ArgumentNullException` and a blank provider an `ArgumentException`. It SHALL implement `IUsageRepository` as
`StoreUsageRepository` over an `IAsyncBulkStore<UsageRecordModel>` that also implements `IAsyncAggregatableStore`,
refusing any other store at construction with `ArgumentException`; every total SHALL be one `AggregateAsync` (count of
`Guid`, sum of `TotalTokens`, sum of `EstimatedCostUsd`) over a half-open `[from, to)` window on `RecordedAt`, provider
summaries grouped by `Provider` and ordered ordinally, and a project with no calls in the window SHALL yield `null`.
Both interfaces take a `CancellationToken`.

#### Scenario: A second save replaces the first

- **Given** a `StoreCircuitBreakerStore` over InMemory
- **When** two states for `"openai"` are saved
- **Then** `LoadAllAsync` returns one state, the later one

#### Scenario: A store that cannot aggregate is refused

- **Given** an `IAsyncBulkStore<UsageRecordModel>` that does not implement `IAsyncAggregatableStore`
- **When** `new StoreUsageRepository(store)` runs
- **Then** `ArgumentException` naming the store type is thrown

#### Scenario: Spend for one provider

- **Given** usage rows for `"Claude"` and `"OpenAI"` today
- **When** `GetTotalSpendAsync(today, tomorrow, "Claude")` is called
- **Then** only Claude's cost is summed, matched by the store's own string equality

### Requirement: Implementation plans track step state, retries and metrics

The system SHALL model a plan as `ImplementationPlan` with ordered `ImplementationStep`s and a 0-based
`CurrentStepIndex`. `AdvanceToNextStep` SHALL stop at `Steps.Count`; `CurrentStep` SHALL be null past the end;
`ProgressPercentage` SHALL be completed × 100 / count rounded down (0 for no steps). `MarkInProgress`, `MarkCompleted`
(which also runs `CaptureLessonsLearned`) and `MarkFailed` SHALL set the status, timestamps and a log entry, and every
mutator SHALL touch `UpdatedAt`. `GetAssignedSteps(worker)` SHALL return every step when no assignment exists at all,
none for an unassigned worker once the plan is split, and only in-range indices otherwise. A step's
`RetryOrFail(error, category, retryable)` SHALL keep the error and category and, when retryable with retries left
(`RetryCount < MaxRetries`, default 3), return the step to `Pending` with `RetryCount + 1` and cleared times; otherwise
it SHALL `Fail` — with `"Max retries ({n}) exhausted. Last error: {error}"` when retryable — and return false.
`RetryOrFail(error, LlmFailure)` SHALL be retryable exactly when the failure is transient (TASK-531).
`CaptureLessonsLearned` SHALL add each entry at most once: resolved issues and "required N iterations" lessons only for
completed steps that were retried, an average-iterations pattern when more than one completed step took 1–3
iterations, and a files-touched lesson. `GetAggregatedMetrics` SHALL sum step counts, iterations, tokens and durations.
`PlanStatus`, `StepStatus`, `AgentTaskStatus` and the escalation enums SHALL keep pinned numeric values
(`StepStatus.Skipped = 3`, `Failed = 4`), and `AgentTaskRecord.ErrorCategory` / `ImplementationStep.ErrorCategory`
SHALL be the typed `ErrorCategory` (TASK-531).

#### Scenario: A transient LLM failure is retried

- **Given** a step with `MaxRetries = 3` and `RetryCount = 0`
- **When** `RetryOrFail("rate limited", new LlmFailure(RateLimited, 429))` is called
- **Then** it returns true, `Status = Pending`, `RetryCount = 1` and `ErrorCategory = Transient`

#### Scenario: Retries run out

- **Given** a step with `RetryCount = 3` and `MaxRetries = 3`
- **When** `RetryOrFail("boom", ErrorCategory.Transient, true)` is called
- **Then** it returns false, `Status = Failed` and `ErrorMessage = "Max retries (3) exhausted. Last error: boom"`

#### Scenario: A split plan gives an unknown worker nothing

- **Given** a plan with `AssignSteps("w1", [0, 1])`
- **When** `GetAssignedSteps("w2")` is called
- **Then** an empty list is returned, never the whole plan

#### Scenario: A slow step that was never retried teaches nothing

- **Given** a completed step that took 20 iterations with `RetryCount = 0`
- **When** `MarkCompleted` runs
- **Then** no "required 20 iterations" lesson is recorded

### Requirement: Step order and parallel groups follow file dependencies

The system SHALL, in `StepDependencyAnalyzer`, treat "step A modifies a file step B creates" as A depending on B.
`SuggestOptimalOrder` SHALL return a dependencies-first order by post-order DFS (CR-H003), silently breaking a cycle.
`ValidateOrdering` SHALL list each step that modifies a file a later step creates. `Reorder(plan)` SHALL refuse with
`InvalidOperationException` a plan that has started (a non-pending step or a moved `CurrentStepIndex`) or has step
assignments, return false when the order is unchanged, and otherwise reorder, renumber `Index` 1..n and log it.
`AnalyzeParallelGroups` SHALL greedily group, in plan order, steps none of whose modified or created files were touched
by an earlier member of the group. `HasDependency` SHALL be true when either step creates what the other modifies, or
both modify or both create the same file.

#### Scenario: Creator moves before modifier

- **Given** steps `1: modify a.cs` and `2: create a.cs`
- **When** `Reorder(plan)` runs
- **Then** the create step becomes step 1, the modify step step 2, and `ValidateOrdering` then returns no violations

#### Scenario: A started plan is not reordered

- **Given** a plan whose first step is `InProgress`
- **When** `Reorder(plan)` is called
- **Then** `InvalidOperationException` is thrown and the steps are unchanged

#### Scenario: Two steps editing one file run in separate groups

- **Given** steps `1: modify a.cs`, `2: modify a.cs`, `3: create b.cs`
- **When** `AnalyzeParallelGroups` runs
- **Then** the groups are `[1, 3]` and `[2]`

### Requirement: Step validators check that a step's work exists

The system SHALL run, in `StepValidationService.ValidateStepAsync`, every validator in order (by default
`FileCreation`, `FileModification`, `ContentExpectation`), count one `ValidationAttempts`, prefix each issue with
`[{validator}]`, record a validator that throws as the issue `"Validation error: {message}"` (never a pass), let
cancellation propagate, and succeed only with no issues. Step paths SHALL resolve under the working directory unless
rooted, with no workspace confinement. `FileCreation` SHALL require every `FilesToCreate` to exist;
`FileModification` every `FilesToModify` to exist and, once the step has started, to have been written after
`StartedAt`; `ContentExpectation` SHALL pass a step with no non-blank expected content, fail one that declares content
but names no files, report an unreadable file, and require each expected string (ordinal) in at least one existing file.

#### Scenario: Touching a file without doing the work

- **Given** a step expecting `"GetUserById"` in `a.cs`, and `a.cs` modified but without that text
- **When** `ValidateStepAsync` runs
- **Then** it fails with `[ContentExpectation] Expected content not found: 'GetUserById' in [a.cs]`

#### Scenario: A throwing validator fails the step

- **Given** a custom validator that throws `IOException("locked")`
- **When** `ValidateStepAsync` runs
- **Then** `AllIssues` contains `[{name}] Validation error: locked` and `Success` is false

### Requirement: Tasks dispatch in-process through a callback

The system SHALL let `DirectTaskDispatcher` (`IsDistributed = false`) dispatch a `TaskAssignment` by awaiting its
callback with the assignment and token, and, without a callback, SHALL log a warning and dispatch nothing. A
`TaskAssignment` SHALL default `MaxIterations` to 100 and generate a `CorrelationId`.

#### Scenario: No callback drops the task

- **Given** a `DirectTaskDispatcher` constructed without a callback
- **When** `DispatchTaskAsync(assignment)` is awaited
- **Then** it completes without error, the task is not run, and a warning naming the task id is logged
