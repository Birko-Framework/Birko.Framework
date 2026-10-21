---
id: TASK-716
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [ ] Each reproduced defect fixed, with a regression test that fails without the fix.
- [ ] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Implementation plan

_Populated by `/tasks plan TASK-716` — leave empty until then._
