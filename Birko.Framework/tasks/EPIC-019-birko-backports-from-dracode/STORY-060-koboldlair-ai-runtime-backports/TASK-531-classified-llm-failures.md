---
id: TASK-531
parent: STORY-060
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# LlmResponse carries a classified failure (status, transient, retry-after), not just a string

## Context

DraCode's `Services/ErrorClassifier.cs` (168) decides transient vs permanent by matching substrings of error text:

- `"500"` and `"timeout"` match unrelated text
- Unknown is documented as permanent, but any non-empty unknown text returns Transient
- `"quota exceeded"` (usually billing) counts as transient

It exists because `LlmResponse.Error(string)` keeps only a message, while `LlmProviderBase` knows the `HttpStatusCode` and
already has `IsRetryableStatusCode`. `AgentTaskRecord.ErrorCategory` is an untyped `string?`.

Adopted in the consumer by DraCode TASK-129 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [x] `LlmResponse` (`Birko.AI.Contracts`) carries the status code, an `IsTransient` flag and retry-after when known
- [x] Classified once, in `LlmProviderBase`; providers that bypass it are covered
- [x] `ErrorCategory` is typed
- [x] A text classifier, if shipped at all, is a documented fallback for tool output only
- [x] Tests: 429 with Retry-After, 5xx, 400, 401/402 billing, network timeout, cancellation (not a failure)

## Human test plan

N/A — library behaviour, covered by unit tests.

## Resolution (2026-10-17)

**Measured first** (a probe on HEAD, Ollama and Z.AI through a hanging handler): a caller's cancellation came back as
`StopReason = "error"`, `"Error calling … API: A task was canceled."` — every provider's catch-all swallowed it.

**Contract (Birko.AI.Contracts):** `LlmFailure(Kind, StatusCode, RetryAfter)` with `IsTransient` and `Category`;
`LlmFailureKind` and `ErrorCategory` (`Transient = 0`, `Permanent = 1`, DraCode's names and order), pinned values.
`LlmResponse.Failure` plus `IsTransient` / `StatusCode` / `RetryAfter`; `Error(message, failure = null)` is `Unknown`
(permanent) without one.

**Classified once, in `LlmProviderBase`:** `ClassifyResponse` (status, Retry-After read from the disposed response's
headers, and a 429 with `error.code`/`type` `insufficient_quota` is `Billing`, the case DraCode called transient),
`ClassifyException` (network, the client's own timeout, unreadable body), `IsCallerCancellation`, `StreamingFailure`,
and `ConnectStreamingAsync`, which `SendStreamingWithRetryAsync` now wraps. Every HTTP provider (Claude, OpenAI, Azure,
Gemini, Ollama, Z.AI, GitHub Copilot, and the OpenAI-compatible base under DeepSeek, Groq, Mistral, OpenRouter,
LM Studio, llama.cpp, vLLM, SGLang) uses them on every sync and streaming path; each catch-all is
`when (!IsCallerCancellation(ex, cancellationToken))`. A Claude error object in a 200 body is classified by its
documented `error.type`. No text classifier is shipped.

**Typed:** `ErrorCategory` on `AgentTaskRecord` and `ImplementationStep` (breaking, CHANGELOG has the signatures), and
`RetryOrFail(error, LlmFailure)`. Birko.AI.Orchestration now imports Birko.AI.Contracts, an edge the documented graph
already drew; all three consumers importing Orchestration already import Contracts.

**Found on the way:** `GitHubCopilotProvider`'s streaming 401 refresh tested `response is { StatusCode: Unauthorized }`,
which the streaming connect never returns, so the refresh never ran. Now read from the classification.

**Tests:** `ClassifiedFailureTests`, 8 providers × {400, 401, 402, 404, 429 quota, 500, 503} sync and streaming, 429
with Retry-After, network, client timeout, cancellation (sync and streaming), not configured, Claude body errors, an
unreadable body, the Copilot refresh, the status map and pinned values; `ClassifiedRetryTests` (3). To test each provider
for real, the HTTP providers gained the optional `HttpMessageHandler` seam Ollama and Z.AI already had. **Red:** the
cancellation on HEAD (probe above); mutations, each caught: Claude's catch-all without the filter (2 red), the old
Copilot condition (1), no quota check (16). Regression: every importer of AI.Contracts / AI / AI.Providers /
AI.Orchestration: Agents 58, Orchestration 42, Providers 218, Resilience 13, Resilience.Data 24, AI 28.
