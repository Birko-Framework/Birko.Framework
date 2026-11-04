---
id: TASK-716
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-11]
pr: null
github-issue: null
jira-key: null
---

# LLM providers: response parsing drops calls and errors

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. Ollama reads tool-call `arguments` as a string while the native reply sends an object. Gemini lets the last part decide the stop reason, dropping a function call followed by text; a blocked Gemini stream returns an error with no message. Claude ignores a mid-stream `error` event and keeps a cut-off streamed tool call. The OpenAI-compatible parser ignores `finish_reason`/`usage` and fails on `"error": null`. Copilot's auth exception is thrown before the `try`. `OrchestratorAgent.ExtractTextFromContent` returns a non-text block's text.

Evidence: OllamaProvider.cs:93; GeminiProvider.cs:289,552; ClaudeProvider.cs:497; OpenAiCompatibleProviderBase.cs:149,177; GitHubCopilotProvider.cs:43.

## Acceptance criteria

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-04, before the fix)

`tests/Birko.AI.Providers.Tests/Task716ResponseParsingTests.cs` and
`tests/Birko.AI.Agents.Tests/Task716OrchestratorTextTests.cs`, each provider fed a canned body through its handler seam.

| Claim | Result |
|---|---|
| Ollama reads `arguments` as a string; `/api/chat` sends an object | **reproduced:** sync `"Error calling Ollama API: … requires an element of type 'String' … 'Object'"`; the stream threw |
| Gemini's last part decides the stop reason | **reproduced:** `[functionCall, text]` read `end_turn`, so the call never ran |
| A blocked Gemini stream errors with no message | **reproduced:** `ErrorMessage` null (the agent says "Unknown LLM error") |
| Claude ignores a mid-stream `error` event | **reproduced:** an `overloaded_error` after half the text read `end_turn` |
| Claude keeps a streamed tool call cut off by the token limit | **reproduced:** the `max_tokens` turn kept the tool_use block with `_raw` input |
| OpenAI-compatible parser fails on `"error": null` | **reproduced:** `"Error calling Local API: … requires … 'Object' … 'Null'"` |
| … ignores `finish_reason` | **reproduced:** a `length` completion with cut-off arguments failed to parse (an error reply), with whole ones it ran |
| … ignores `usage` | **reproduced:** `Usage` null (TASK-715's rate limit now counts the request anyway; cost needs this) |
| Copilot's sign-in exception is thrown before the `try` | **reproduced:** sync and streaming both threw the `IOAuthClient`'s exception |
| `ExtractTextFromContent` returns a non-text block's text | **reproduced:** a single `thinking` block gave its text; a single JSON block object gave its raw JSON |
| Found: Gemini's stream flattens nested `args` to strings (the sync path had CR-L011's fix) | **reproduced:** `opts` came back as a string |

## Implementation plan

Ollama reads `arguments` as an object or a string (one `ReadArguments`, both parsers). Gemini: any `functionCall` makes a
tool turn; the stream deserialises `args` whole and a blocked stream is `LlmResponse.Error` with the reason. Claude's
stream turns an `error` event into an error response (classified by `ClassifyErrorType`) and drops tool calls on
`max_tokens`. `OpenAiCompatibleProviderBase.ParseResponse` becomes `ParseOpenAiStyleResponse` (rule 16, one parser), which
gains a provider name for errors and fills a missing tool-call id. Copilot's sign-in moves inside the error handling.
`ExtractTextFromContent` reads a single block as a list item is read.

## Built (2026-11-04)

18 tests: 14 failing on the old code, 4 guarding what already worked (Ollama string arguments, a compatible call without
an id, a compatible error object, a single text block). AI.Providers 261, AI.Agents 86, AI 75, AI.Resilience 18,
AI.Resilience.Data 24, AI.Orchestration 42 green. Spec `llm-provider-and-agents.md` updated (scoped; `generated-at` left).
