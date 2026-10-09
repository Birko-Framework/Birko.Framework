---
id: TASK-546
parent: STORY-060
feature: null
status: todo
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

- [ ] Each provider's request building is checked for a user turn following a tool-result user turn — measured
      against the provider's documented contract (or a live call where one is cheap), not assumed
- [ ] Where a provider cannot accept it, the turns are merged in that provider's message conversion (not in
      `Agent`), so the conversation the hook sees stays as written
- [ ] Provider-level tests: a tool-result turn followed by a text user turn produces a valid request body for each
      provider that needed a change, and is passed through unchanged for the others
- [ ] `AgentIterationContext.AppendUserMessage`'s XML doc states the outcome

## Out of scope

- The hook API itself — [[TASK-527]]

## Human test plan

N/A — library behaviour, covered by provider-level unit tests.

## Implementation plan

_Populated by `/tasks plan TASK-546` — leave empty until then._
