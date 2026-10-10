---
id: TASK-546
parent: STORY-060
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-09
depends-on: [TASK-527]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A hook-injected message after a tool-result turn makes two consecutive user turns

## Context

Found while closing [[TASK-527]]. `AgentIterationContext.AppendUserMessage` (`Birko.AI/Agents/AgentHooks.cs`) adds
a plain user message before the model call. After a tool batch the conversation already ends in a user turn (the
`tool_result` list), so the injection produces **two user turns in a row**. DraCode's forked loop has done this in
production with its reflection prompt on its providers, so it works there; nobody has checked the rest.

Providers differ: Anthropic accepts consecutive user turns, OpenAI-compatible APIs accept them, while some (Gemini's
`contents` historically demanded user/model alternation) may reject the request. `Birko.AI.Providers` has 15
providers (`GeminiProvider`, `ClaudeProvider`, the `OpenAiCompatibleProviderBase` family, …) and none of them merges
or checks turns today (no match for "alternat" / "consecutive").

## Acceptance criteria

- [x] Each provider's request building is checked for a user turn following a tool-result user turn — measured
      against the provider's documented contract (or a live call where one is cheap), not assumed
- [x] Where a provider cannot accept it, the turns are merged in that provider's message conversion (not in
      `Agent`), so the conversation the hook sees stays as written
- [x] Provider-level tests: a tool-result turn followed by a text user turn produces a valid request body for each
      provider that needed a change, and is passed through unchanged for the others
- [x] `AgentIterationContext.AppendUserMessage`'s XML doc states the outcome

## Out of scope

- The hook API itself — [[TASK-527]]

## Human test plan

N/A — library behaviour, covered by provider-level unit tests.

## Implementation plan

Done directly.

## Resolution (2026-10-10)

**Measured per provider** (no live calls — documented contracts, plus one production report):

| Provider | Contract | Change |
|---|---|---|
| Gemini | Gemini API docs: `contents` roles should alternate user / model; a forum report of a 400 on consecutive user turns, AI Studio itself merges them | **merge** consecutive same-role messages into one content, parts in order (`BuildRequestPayload`) |
| Mistral | "Unexpected role 'user' after role 'tool'" — the API needs an assistant after a tool message; Zed shipped the fix (zed-industries/zed#31736, 2025-06-06) with a `" "` assistant placeholder | **placeholder** `{ role: assistant, content: " " }` between a `tool` and a following `user` message — tool and user cannot merge |
| Claude | Messages API: consecutive user / assistant turns are combined into one | none (pinned by a test) |
| OpenAI-style: OpenAI, Azure, Copilot, ZAi, DeepSeek, Groq, OpenRouter | tool results are `role: tool` messages, and `user` after `tool` is valid | none (pinned) |
| Local: vLLM, llama.cpp, LM Studio, SGLang, Ollama | the model's chat template decides | none — recorded in `AppendUserMessage`'s XML doc; not fixable per provider |

New `OpenAiCompatibleProviderBase.BuildMessages` (virtual; both payloads use it) — the seam Mistral overrides. The
merge / placeholder exist in the request only; the conversation a hook sees is unchanged (asserted).

Found on the way:

- `OpenAiProvider` and `OllamaProvider` each carried a private `BuildMessages` + `BuildRequestPayload` pair, never
  called, that passed message content through unconverted (Anthropic-style blocks to an OpenAI API) — it nearly
  misled this check. Deleted (rule 53).
- `GeminiProvider` sends a random GUID as `functionResponse.name` instead of the function name → filed [[TASK-554]].

Docs: `AgentIterationContext.AppendUserMessage` XML, Birko.AI.Providers CLAUDE.md § Turn order.

**Proof.** `tests/Birko.AI.Providers.Tests/ProviderTurnOrderTests.cs` (9): Mistral placeholder (sync + streaming),
none when the model replied, one after several tool messages; OpenAI-compatible pass-through (both payloads);
Gemini merge and alternating-unchanged; Claude pass-through. **Mutation** — Gemini and Mistral restored: 4 fail.
Providers 30/30.
