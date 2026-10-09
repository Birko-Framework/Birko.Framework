---
id: TASK-544
parent: STORY-060
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-10-09
depends-on: [TASK-527]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# The streaming fallback re-runs tools that already executed

## Context

Found while planning [[TASK-527]]. In `Birko.AI/Agents/Agent.cs` (lines ~170–185), `RunWithHistoryAsync`
snapshots the conversation and wraps **the whole** `RunWithHistoryStreamingAsync` call in
`catch (Exception ex) when (ex is not OperationCanceledException && _options.StreamingFallbackToSync)`. Only
iteration 1 streams, but the try covers every iteration and every tool call. So a non-cancellation failure in
iteration 2 or later — a provider error, a timeout, a tool that throws — restores the pre-run snapshot and
re-runs the entire conversation in sync mode. Every tool the streaming attempt already executed (file writes,
database calls, network sends) runs a **second** time, and the user is told only "Streaming failed".

The CR-L001 comment above the snapshot explains why the conversation is restored (no dangling tool_use), but not
that the side effects outside the conversation cannot be restored. `StreamingFallbackToSync` is meant to cover
"streaming is unavailable for this provider/request", not "anything failed later".

TASK-527 folds the two loops into one and excludes `AgentHookException` from the filter; it does not change
what the fallback covers. Implement after TASK-527 lands, on its single loop.

## Acceptance criteria

- [ ] The sync fallback covers only the failure of the **streaming request itself** (iteration 1's
      `SendMessageStreamingAsync` and the stream's consumption), never a later iteration or a tool call
- [ ] A failure after the first tool has executed propagates; no tool runs twice
- [ ] No `IAgentHooks` member fires twice for the same iteration or tool call (TASK-527's hooks currently re-fire
      on the replay, so a hook's counters and telemetry double-count); remove the ⚠ replay note from
      `IAgentHooks`' XML doc once fixed
- [ ] Iteration 1's streaming failure still falls back to a sync request for **that** iteration, without
      re-running anything
- [ ] Regression test proven to fail on the current code: a tool counter reads 1, not 2, when iteration 2's
      provider call throws with `EnableStreaming = true` and `StreamingFallbackToSync = true`
- [ ] CHANGELOG entry if the behaviour change is observable to consumers (a later-iteration error now throws
      instead of being retried)

## Out of scope

- Hook behaviour — [[TASK-527]]
- Retry policy for transient provider errors (that is `Birko.AI.Resilience`'s job, not a fallback's)

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan

_Populated by `/tasks plan TASK-544` — leave empty until then._
