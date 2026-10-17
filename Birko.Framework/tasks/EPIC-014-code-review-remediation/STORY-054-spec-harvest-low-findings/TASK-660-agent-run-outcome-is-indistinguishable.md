---
id: TASK-660
parent: STORY-054
feature: FEATURE-014
status: todo
priority: P3
assignee: ai
created: 2026-10-18
depends-on: []
blocks: []
findings: [SH-L159]
pr: null
github-issue: null
jira-key: null
---

# A run's outcome cannot be told apart: success, LLM failure, not configured and exhaustion all return a conversation

## Context

Spawned by [[TASK-173]] (SH-L159). It was confirmed, and it is too large for a triage fix because it changes the
public shape of `Agent.RunAsync` / `ContinueAsync` / `ResumeAsync`.

Every terminal path of `Agent.HandleResponse` returns `true`, and the run returns the `List<Message>`. A caller cannot
tell a completed task from "LLM request failed", "provider not configured", "maximum iterations reached" or an
unexpected stop reason, except by string-matching the injected `"Error: …"` text. The unexpected-stop branch injects
nothing when `Verbose` is false.

TASK-531 added `LlmResponse.Failure`, so the classified cause now exists at the point the run ends; it is just not
returned.

## Options to decide (consumer-visible)

1. A result type, `AgentRunResult(List<Message> Conversation, AgentRunOutcome Outcome, LlmFailure? Failure)`, from new
   `Run…Async` overloads; the current methods stay and forward.
2. An `OnRunCompleted` hook (`IAgentHooks`) carrying the outcome; no signature changes.
3. Throw on the failure outcomes (`AgentRunException` with the conversation and the failure). This breaks every caller
   that relies on getting the conversation back.

## Acceptance criteria

- [ ] The decision is recorded, and a caller can tell the outcomes apart without matching strings
- [ ] Tests for each outcome: end_turn, error with a classified failure, not configured, max iterations, an unexpected
      stop reason

## Human test plan

N/A — library behaviour, covered by unit tests.
