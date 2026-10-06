---
id: TASK-516
parent: null
feature: null
status: done
priority: P1
assignee: ai
created: 2026-10-06
depends-on: []
blocks: []
findings: [FIELD-020]
pr: null
github-issue: null
jira-key: null
---

# ZAiProvider caps glm-5.3 at 4096 output tokens and reports a truncated reply as a normal end

## Context

Found 2026-10-06 diagnosing DraCode's empty Wyvern replies (DraCode TASK-101; that consumer runs every agent on Z.AI
`glm-5.3`). `ZAiProvider.GetMaxTokensForModel` (`Birko.AI.Providers/Providers/ZAiProvider.cs:~173`) knows only GLM-5/5.1 and
older; `glm-5.3` (and `glm-5.3-flash`) fall to the `_ => 4096` default, so every request asks for at most 4096 output tokens.
Z.AI documents GLM-5.3 with a default `max_tokens` of 65536 and a maximum of 131072. With deep thinking on, the reasoning
counts against those 4096: a long answer runs out and comes back with empty `content`.

`ParseResponse` then sets `StopReason = "end_turn"` for every non-tool reply and never reads `finish_reason`, so a caller
cannot tell a truncated reply from a finished one. `glm-5.3` is also missing from `ValidModels` (a "may not be recognized"
warning on every construction).

## Acceptance criteria

- [x] `glm-5.3` and `glm-5.3-flash` are known models: `glm-5.3` requests `max_tokens` 131072 (the documented maximum, as for `glm-5.1`); `glm-5.3-flash` 65536 (the family default — no first-party maximum found)
- [x] A reply with `finish_reason: "length"` reports `StopReason = "max_tokens"` (non-streaming); other replies are unchanged
- [x] `ZAiProvider` takes an optional `HttpMessageHandler` test seam, as `OllamaProvider` does
- [x] Tests: the request for `glm-5.3` carries the new `max_tokens`; a `length` reply reports `max_tokens`; proven to fail before the fix
- [x] `Birko.AI.Providers.Tests` green

## Out of scope

- Raising `max_tokens` for other providers or unknown models (the 4096 default stays)
- How consumers react to a `max_tokens` stop — DraCode TASK-101

## Human test plan

N/A — covered by automated tests; the consumer's live run (DraCode TASK-101) shows the effect end to end.

## Implementation plan

1. `Models.Glm53` / `Models.Glm53Flash`; add both to `ValidModels` and `GetMaxTokensForModel`.
2. `ParseResponse`: read `choices[0].finish_reason`; `length` → `StopReason = "max_tokens"` after the text/tool branch.
3. Constructor: optional trailing `HttpMessageHandler? handler` used to build the `HttpClient`.
4. `ZAiProviderTests` with a capturing handler.

## Progress log

- 2026-10-06 — `ZAiProviderTests` (4) written first: the `glm-5.3` / `glm-5.3-flash` requests carried `max_tokens` 4096 and a `length` reply reported `end_turn` (3 red); fixed as planned; `Birko.AI.Providers.Tests` 21/21.
