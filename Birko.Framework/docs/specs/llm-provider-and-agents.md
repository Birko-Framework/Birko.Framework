---
area: llm-provider-and-agents
generated-at: c50ed580c4d08391a4ae55a7e8b62c394f6e3469
generated-on: 2026-10-12
sources:
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
  - ../Birko.AI/Agents/Agent.cs
  - ../Birko.AI/Agents/AgentHooks.cs
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
  - ../Birko.AI/Tools/WriteFileTool.cs
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: [FEATURE-006]
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 266
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

#### Scenario: Provider message callback is redirected by the agent

- **Given** a provider whose `MessageCallback` is already set
- **When** an `Agent` is constructed over it with `messageCallback` of `null`
- **Then** `Agent`'s constructor assigns `_llmProvider.MessageCallback = null`, discarding the
  previously assigned callback

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

#### Scenario: Policy with negative MaxRetries performs no request

- **Given** a `RetryPolicy` with `MaxRetries = -1`
- **When** `SendWithRetryAsync` is invoked
- **Then** the loop body never runs and `(null, null)` is returned

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
- **Then** `"{providerName} API Error: 401"` is emitted, the response is disposed and `null` is returned

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
- **Then** only `textBlocks[0].Text` (`"first"`) is carried as `content` and `"second"` is dropped

#### Scenario: Tool-result turn expressed as anonymous objects

- **Given** a `user` `Message` whose `Content` is a list of anonymous objects each with `type`,
  `tool_use_id` and `content` properties (as produced by the agent loop)
- **When** `BuildOpenAiStyleMessages` runs
- **Then** one `{role="tool", tool_call_id, content}` entry is emitted per object and the
  original message contributes no other entry

#### Scenario: Object list whose first item has `type` but no tool-result pair

- **Given** a list of anonymous objects carrying `type` but neither `tool_use_id` nor `content`
- **When** the branch is entered
- **Then** no entry is added for any element and the loop `continue`s, dropping the message entirely

#### Scenario: Object list whose first item lacks `type`

- **Given** a list of plain anonymous objects with no `type` property
- **When** the branch check fails
- **Then** the raw object list is emitted as the `content` of a single `{role, content}` entry

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
`LlmResponse` whose `StopReason` is `"tool_use"` when `choices[0].message.tool_calls` is
non-empty (one `tool_use` `ContentBlock` per call) and `"end_turn"` otherwise (a single `text`
block from `message.content`). A top-level `error` property SHALL yield
`LlmResponse.Error($"API returned error: {type} - {message}")`, and any thrown exception during
parsing SHALL yield `LlmResponse.Error($"Error parsing OpenAI-style response: {ex.Message}")`.
Both error paths SHALL also invoke the supplied message callback with type `"error"`.

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

- **Given** a body with neither `error` nor `choices`
- **When** `result.GetProperty("choices")` throws
- **Then** the result is an error `LlmResponse` with `StopReason = "error"`

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

The system SHALL decode a Server-Sent-Events stream in `ParseSseStream` by accumulating every
`data: ` line into a buffer, flushing the buffer as one chunk on a blank line, terminating the
enumeration on a `data:` payload equal to `[DONE]`, and flushing any residual buffer when the
underlying reader ends.

#### Scenario: Single-line frame

- **Given** the stream `data: {"a":1}\n\n`
- **When** it is parsed
- **Then** one chunk containing `{"a":1}` followed by a newline (from `AppendLine`) is yielded

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
The derived stop reason SHALL be `"tool_use"` when any tool call was captured, `"end_turn"` when
`finish_reason` is `"stop"` or absent, and otherwise the raw `finish_reason` string.

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

- **Given** a stream whose final `finish_reason` is `"length"` with no tool calls
- **When** the stream completes
- **Then** `StopReason` is `"length"`, which the agent's stop-reason switch treats as an
  unexpected reason and stops the loop

#### Scenario: Unparseable chunk is skipped

- **Given** an SSE payload that is not valid JSON
- **When** the parser deserializes it
- **Then** the chunk is skipped and enumeration continues

#### Scenario: Chunk without a delta

- **Given** a chunk with a non-empty `choices` array whose element has no `delta` property
- **When** the tool-capture parser reads it
- **Then** the `TryGetProperty("delta")` guard skips it, while the simpler
  `ParseOpenAiStreamChunks` calls `choices[0].GetProperty("delta")` and throws
  `KeyNotFoundException` out of the enumerator

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

### Requirement: Agent options carry defaults, clone deeply and merge scalar-last-wins

The system SHALL default `AgentOptions` to `Interactive = true`, `MaxIterations = 10`,
`MaxIterationsPerStep = 10`, `Verbose = true`, `WorkingDirectory = "./"`,
`PromptTimeout = 300`, `ModelDepth = 5`, `EnableStreaming = false`,
`StreamingFallbackToSync = true`, an empty `AllowedExternalPaths`
and a null `DefaultPromptResponse`. `Clone()` SHALL copy every field and allocate a new
`AllowedExternalPaths` list. `Merge(other)` SHALL return immediately on a null `other`, copy
every value-typed field unconditionally, and copy `WorkingDirectory`, `DefaultPromptResponse`,
`AllowedExternalPaths` and `OnLlmResponseReceived` only when the incoming value is
non-empty/non-null.

#### Scenario: Clone isolates the path list

- **Given** options with `AllowedExternalPaths = ["C:\\shared"]`
- **When** `Clone()` is called and the clone's list is mutated
- **Then** the original list is unaffected

#### Scenario: Merging a default instance resets scalars

- **Given** options with `MaxIterations = 50` and `Verbose = false`
- **When** `Merge(new AgentOptions())` is called
- **Then** `MaxIterations` becomes 10 and `Verbose` becomes `true`, because scalar fields are
  copied without an "is specified" guard

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

#### Scenario: Null default prompt response is omitted

- **Given** options with `DefaultPromptResponse = null`
- **When** `ToDictionary` runs
- **Then** the result contains no `"defaultPromptResponse"` key

### Requirement: Agent construction wires tools and callbacks

The system SHALL throw `ArgumentNullException` from the `Agent` constructor when
`llmProvider` is null, SHALL substitute a fresh `AgentOptions` when `options` is null, SHALL
build the tool list via the overridable `CreateTools()` and SHALL assign the agent's message
callback and options to every tool. `AddTool` SHALL apply the same wiring to a late-added tool;
`RebuildTools()` SHALL rediscover the tool list and re-wire it; `RemoveTool(name)` SHALL remove
the first tool whose `Name` matches exactly and return whether one was removed.

#### Scenario: Default tool catalogue

- **Given** an `Agent` subclass that does not override `CreateTools`
- **When** it is constructed
- **Then** `Tools` contains nine tools named `list_files`, `read_file`, `write_file`,
  `edit_file`, `append_to_file`, `search_code`, `run_command`, `display_text`, `ask_user`

#### Scenario: Null provider

- **Given** `llmProvider = null`
- **When** the constructor runs
- **Then** `ArgumentNullException` is thrown for `llmProvider`

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

The system SHALL iterate from 1 to `maxIterations ?? Options.MaxIterations` in one loop shared
by the streaming and non-streaming paths, firing the iteration hook (when `Hooks` is set) and
then checking cancellation at the top of every iteration, sending the whole conversation plus
tools plus `SystemPrompt` to the provider, invoking `Options.OnLlmResponseReceived` after each response,
appending the assistant turn to the conversation, then dispatching on the stop reason. When the
loop finishes without a terminal stop reason it SHALL emit a `"warning"` (when `Verbose`) stating
the maximum was reached and return the conversation.

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
`"NotConfigured"` emits the provider-not-configured error and stops; any other value (including
`null`) emits a `"warning"` when `Verbose` and stops. For the `"error"` and `"NotConfigured"`
cases the system SHALL ensure the assistant turn carries the error text.

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
- **Then** an empty result list is still appended as a `user` message and the loop continues

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
plain `Dictionary` instances inside `AgentFactory`, SHALL resolve an agent type through a
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

#### Scenario: Unregistered type message uses the requested name

- **Given** `RegisterAlias("docs", "writer")` with no `"writer"` factory
- **When** `Create(provider, null, "docs")` is called
- **Then** the `ArgumentException` message names `"docs"` (the requested type), not `"writer"`

#### Scenario: Registry is not thread-safe

- **Given** two threads calling `Register` concurrently
- **When** both mutate the plain `Dictionary`
- **Then** behaviour is undefined, unlike `LlmProviderFactory`, which uses a
  `ConcurrentDictionary` for the same purpose

### Requirement: Tools declare a name, description and JSON input schema

The system SHALL require every `Tool` to expose `Name`, `Description` and `InputSchema`, to
implement `ExecuteAsync(workingDirectory, input, cancellationToken)` returning a string, and
SHALL give each tool an optional `MessageCallback` (raised via the protected `SendMessage`) and
an optional `AgentOptions` reference used for path allow-listing and interactivity.

#### Scenario: Tool result string is the only channel back to the model

- **Given** any built-in tool
- **When** execution succeeds or fails
- **Then** the outcome is expressed entirely as the returned string; failures are conveyed as
  strings beginning with `"Error"` rather than as thrown exceptions

### Requirement: File tools confine paths to the workspace and allow-list

The system SHALL check every file path against `PathHelper.IsPathSafe(fullPath,
workingDirectory, Options?.AllowedExternalPaths)` before touching the filesystem in
`read_file`, `write_file`, `edit_file`, `append_to_file` and `list_files`, and SHALL return
`"Error: Access denied. Path must be in workspace or an allowed external path."` when the check
fails. `search_code` SHALL instead call the two-argument
`PathHelper.IsPathSafe(targetDir, workingDirectory)` overload, ignoring
`AllowedExternalPaths`, and SHALL return `"Error: Access denied. Directory must be in
{workingDirectory}"`.

#### Scenario: Traversal outside the workspace is denied

- **Given** `workingDirectory = "C:\\repo"` and `file_path = "..\\..\\secrets.txt"`
- **When** `read_file` executes
- **Then** the access-denied error string is returned and no file is read

#### Scenario: Allow-listed external path is readable

- **Given** `Options.AllowedExternalPaths = ["C:\\shared"]` and a path resolving under it
- **When** `read_file` executes
- **Then** the safety check passes and the file content is returned

#### Scenario: search_code cannot reach an allow-listed path

- **Given** the same allow-listed `C:\shared` and `directory` pointing at it
- **When** `search_code` executes
- **Then** the two-argument overload rejects it and the access-denied error is returned, diverging
  from every other file tool

#### Scenario: Absolute-looking path handling differs between tools

- **Given** `file_path = "/etc/passwd"`
- **When** `read_file` or `list_files` runs
- **Then** the leading `/` is stripped and the path is resolved relative to the workspace,
  whereas `write_file`, `edit_file` and `append_to_file` pass it straight to `Path.Combine`,
  where a rooted path discards the workspace prefix and the safety check then denies it

### Requirement: read_file returns raw content or an error string

The system SHALL require a non-empty `file_path`, normalise the workspace with
`Path.GetFullPath`, strip a single leading `/`, resolve the full path, apply the safety check and
return the file's entire text; any exception SHALL be returned as
`"Error reading file: {message}"`.

#### Scenario: Missing file

- **Given** a safe path that does not exist
- **When** `read_file` executes
- **Then** the `FileNotFoundException` is caught and `"Error reading file: …"` is returned

#### Scenario: Missing file_path key

- **Given** an input dictionary without `file_path`
- **When** `input["file_path"]` throws `KeyNotFoundException`
- **Then** the catch block returns `"Error reading file: …"`

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
- **Then** `"Error editing file: old_text is required"` is returned

#### Scenario: Missing file

- **Given** a safe path that does not exist
- **When** `edit_file` executes
- **Then** `"Error: File does not exist: {path}"` is returned

### Requirement: append_to_file appends and may create the file

The system SHALL append `content` to the target with `File.AppendAllText` — creating the file
when absent — after requiring `file_path`, defaulting `content` to the empty string, defaulting
`create_directories` to `true`, and applying the path safety check; it SHALL return `"OK"` on
success and `"Error appending to file: {message}"` on any exception.

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
`Environment.NewLine`.

#### Scenario: Case-insensitive substring search

- **Given** `query = "todo"` with `case_sensitive` unset
- **When** `search_code` runs
- **Then** lines containing `"TODO"` match via `StringComparison.InvariantCultureIgnoreCase`

#### Scenario: No matches

- **Given** a query occurring nowhere
- **When** `search_code` completes
- **Then** the empty string is returned — there is no explicit "no matches" message

#### Scenario: Invalid regular expression

- **Given** `regex = true` and `query = "["`
- **When** the `Regex` constructor throws
- **Then** `"Error searching code: …"` is returned

#### Scenario: Unreadable file is skipped

- **Given** a locked or binary-but-unlisted file inside the search set
- **When** `File.ReadAllLines` throws
- **Then** that file is skipped and the scan continues over the rest

### Requirement: run_command executes a process and returns its combined output

The system SHALL require a non-empty `command`, an existing `workingDirectory` and a positive
`timeout_seconds` (default 120), and run the process through `Birko.Helpers.ProcessRunner` (TASK-534, the one
runner): both standard streams drained concurrently from the start, the process tree killed on timeout. It SHALL
return stderr appended to stdout when stderr is non-empty or stdout alone otherwise, and SHALL let the caller's
cancellation propagate as `OperationCanceledException`.

#### Scenario: Successful command

- **Given** `command = "git"`, `arguments = "status"` in a valid working directory
- **When** the process exits with empty stderr
- **Then** the captured stdout is returned verbatim

#### Scenario: Command writing to stderr

- **Given** a process producing both stdout and stderr
- **When** it exits
- **Then** the result is stdout, a newline, then stderr — with no indication of the exit code

#### Scenario: Timeout kills the process tree

- **Given** `timeout_seconds = 1` and a process that runs longer
- **When** the timeout elapses
- **Then** `ProcessRunner` kills the tree and reports `TimedOut`, and `"Error: Process timed out"` is returned

#### Scenario: Caller cancellation propagates

- **Given** the agent's `cancellationToken` is cancelled while the process runs
- **When** `ProcessRunner` observes it
- **Then** the process tree is killed and `OperationCanceledException` propagates (before TASK-534 the tool returned
  `"Error: Process timed out"` and swallowed the cancellation)

#### Scenario: Failing command with silent stderr looks successful

- **Given** a command exiting with a non-zero code, empty stderr and empty stdout
- **When** `run_command` returns
- **Then** the empty string is returned; the exit code is never inspected and the result does not
  begin with `"Error:"`, so the agent's failure counter is not incremented

#### Scenario: Invalid working directory

- **Given** a `workingDirectory` that does not exist
- **When** `run_command` executes
- **Then** the `ArgumentException` guard produces `"Error running command: Invalid working directory"`

### Requirement: display_text surfaces text through the message callback

The system SHALL require a non-empty `text`, prepend `title` followed by a newline when a
non-blank title is supplied, emit the composed string as a `"display"` message and return
`"Text displayed successfully"`.

#### Scenario: Title and body

- **Given** `title = "Summary"` and `text = "3 files changed"`
- **When** `display_text` executes
- **Then** the `"display"` message body is `"Summary\n3 files changed"`

#### Scenario: Blank text

- **Given** `text = "   "`
- **When** `display_text` executes
- **Then** `"Error: text parameter is required"` is returned and no message is emitted

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
- **Then** `"Error: Prompt timed out after 30 seconds."` is returned and the abandoned prompt task
  is never awaited

#### Scenario: Cancellation is reported as a timeout

- **Given** a cancelled `cancellationToken`
- **When** `Task.Delay(..., cancellationToken)` completes first because it was cancelled
- **Then** the timeout branch is taken and the prompt-timeout error is returned instead of an
  `OperationCanceledException`

#### Scenario: Context is prepended to the question

- **Given** a non-blank `context`
- **When** the prompt is emitted
- **Then** the message body is `"Context: {context}\n\nQuestion: {question}"` while the callback
  still receives question and context as separate arguments
