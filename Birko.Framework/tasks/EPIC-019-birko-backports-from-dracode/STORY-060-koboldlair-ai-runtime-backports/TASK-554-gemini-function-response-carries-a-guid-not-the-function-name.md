---
id: TASK-554
parent: STORY-060
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-10
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# GeminiProvider sends a GUID as functionResponse.name, not the function's name

## Context

Found while doing [[TASK-546]]. `GeminiProvider` parses a model's `functionCall` into a `tool_use` block with
`Id = Guid.NewGuid().ToString()` (`GeminiProvider.cs` ~279, ~506), and turns the agent's `tool_result` back into
`functionResponse { name = tool_use_id }` (~121, ~150). So the response carries a random GUID where Gemini's
contract has the **function name** of the call it answers — the model cannot match the result to the call (by
name), and a strict API version may refuse the request. Not measured live; read from code against the
`FunctionResponse.name` field of the Gemini API.

## Acceptance criteria

- [x] The `functionResponse.name` is the called function's name (resolve it from the preceding assistant turn's
      `tool_use` block with that id — the tool_result itself carries only the id)
- [x] Measured against Gemini's documented contract, or a live call if cheap
- [x] Provider-level test: a tool_use → tool_result round trip produces `functionResponse.name == "<tool name>"`,
      proved to fail on the current code

## Out of scope

- Turn order — [[TASK-546]]

## Human test plan

N/A — library behaviour, covered by provider-level unit tests.

## Implementation plan

Done directly.

## Resolution (2026-10-10)

`GeminiProvider.BuildRequestPayload` first collects `tool_use` id → function name from the assistant turns
(`FunctionNamesById` — `ContentBlock` lists and a JSON-restored `JsonElement` array alike), and one helper,
`FunctionResponse`, builds both former copies of the part with the resolved **name**. A result whose call is not in
the conversation (trimmed history) keeps the id — nothing better is known. Contract: the Gemini API's
`FunctionResponse.name` is the name of the function the response answers (same field the `functionCall` carries).
Not measured live.

**Proof.** 3 tests in `ProviderTurnOrderTests` (name from a `ContentBlock` call, from a JSON conversation, id
fallback). **Mutation** — previous `GeminiProvider` restored: 2 fail. Providers 33/33.
