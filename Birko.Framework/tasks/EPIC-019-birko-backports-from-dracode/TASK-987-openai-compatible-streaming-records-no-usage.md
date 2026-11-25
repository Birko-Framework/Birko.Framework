---
id: TASK-987
parent: EPIC-019
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: [TASK-984]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A streamed call through an OpenAI-compatible provider records no token usage

## Context

Found by DraCode while running TASK-984's human test (2026-11-25): Dragon's streamed replies through vLLM record no
usage, so cost reports and token-based rate limits miss them; only non-streamed calls are recorded. Older than
TASK-984 and not caused by it.

Cause, read in the code: `OpenAiCompatibleProviderBase.BuildStreamingPayload` sends `stream: true` without
`stream_options: { include_usage: true }`, so an OpenAI-compatible server sends no final usage chunk. The parser
(`LlmProviderBase.ParseOpenAiStreamChunksWithToolCapture`) already reads a `usage` chunk when one arrives, and
`TrackedLlmProvider.WrapStreamForUsageTracking` records it, so the request is the only missing piece. Every provider on
that base is affected: DeepSeek, Groq, LlamaCpp, LmStudio, Mistral, OpenRouter, Sglang, Vllm.

## Acceptance criteria

- [x] The streaming payload asks for usage (`stream_options.include_usage`), and a streamed call through
      `TrackedLlmProvider` records prompt and completion tokens.
- [x] A server that rejects `stream_options` does not lose the call: measure which of the eight providers' servers accept
      it, and let a provider opt out (an override, or retry without it) where one does not.
- [x] Tests: a fake SSE stream with a final usage chunk yields recorded usage; the request body carries the option. Red
      before the change.
- [x] Recent Updates entry.

## Evidence

Streaming requests on `OpenAiCompatibleProviderBase` now send `stream_options: { include_usage: true }`;
`RequestStreamUsage` (virtual, default `true`) is the per-provider opt-out. `OpenAiProvider` (not on the base, same
stream format) sends it too. The shared parser also reads `x_groq.usage`.
- **Which servers accept it**, from their docs (2026-11-25), not measured against live servers: vLLM, SGLang, llama.cpp
  (opt-in since #16052), LM Studio 0.3.18+, DeepSeek (400 only if sent without `stream: true`, which the non-streaming
  payload does not do), OpenAI. Groq accepts it and reports under `x_groq.usage`. Mistral: documented only by a
  third-party mirror of its public spec. OpenRouter deprecated it (no effect, usage always sent): opted out.
  `AzureOpenAiProvider` left unchanged: its pinned `api-version=2024-02-15-preview` predates the option (added in
  2024-09-01-preview), so sending it would risk a 400 on every streamed call. A Mistral or old LM Studio server that
  rejects the field would now fail streaming; the override is the remedy, and the human test below covers vLLM.
- `Task987StreamUsageTests` (7): vLLM, Groq and DeepSeek streaming payloads carry the option; OpenRouter's does not; a
  non-streaming payload does not; an OpenAI stream asks for usage and records 12 + 3 tokens from the final chunk; a
  Groq-shaped stream records 20 + 5 from `x_groq.usage`. **Red** 5/7 before the change (the two guards held already).
- `Birko.AI.Providers.Tests` 268/268, `Birko.AI.Tests` 75/75, `Birko.AI.Agents.Tests` 86/86,
  `Birko.AI.Resilience.Tests` 24/24, `Birko.AI.Orchestration.Tests` 42/42; no nullable warnings. Providers CLAUDE.md,
  § Recent Updates (oldest entry rolled to CHANGELOG.md to stay under the 10 KB budget).

## Out of scope

- Providers not on `OpenAiCompatibleProviderBase` (their own streaming formats).

## Human test plan

- [x] In DraCode, a streamed Dragon reply through vLLM appears in the cost report with its token counts.
  - _Run 2026-11-25 by DraCode (Birko.Framework e4a18826, a throwaway server, vLLM provider `lan2`):_ PASS. A streamed
    Dragon reply is now recorded (lan2 / dragon, 3,646 prompt + 43 completion tokens, $0.0369); a 600-word streamed story
    recorded 695 completion tokens. The cost report lists lan2 at 5 requests / 23,284 tokens (2 before the run), finstat
    on its own row. vLLM accepted `stream_options`, no errors.

**Closed 2026-11-25** (on Framework main, e4a18826; not pushed yet).
