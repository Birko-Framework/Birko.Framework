---
id: TASK-987
parent: EPIC-019
feature: null
status: todo
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

- [ ] The streaming payload asks for usage (`stream_options.include_usage`), and a streamed call through
      `TrackedLlmProvider` records prompt and completion tokens.
- [ ] A server that rejects `stream_options` does not lose the call: measure which of the eight providers' servers accept
      it, and let a provider opt out (an override, or retry without it) where one does not.
- [ ] Tests: a fake SSE stream with a final usage chunk yields recorded usage; the request body carries the option. Red
      before the change.
- [ ] Recent Updates entry.

## Out of scope

- Providers not on `OpenAiCompatibleProviderBase` (their own streaming formats).

## Human test plan

- [ ] In DraCode, a streamed Dragon reply through vLLM appears in the cost report with its token counts.
