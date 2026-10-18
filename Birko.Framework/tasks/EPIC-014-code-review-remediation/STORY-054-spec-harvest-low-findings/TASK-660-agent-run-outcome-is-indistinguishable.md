---
id: TASK-660
parent: STORY-054
feature: FEATURE-014
status: done
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

- [x] The decision is recorded, and a caller can tell the outcomes apart without matching strings
- [x] Tests for each outcome: end_turn, error with a classified failure, not configured, max iterations, an unexpected
      stop reason

## Human test plan

N/A — library behaviour, covered by unit tests.

## Outcome (2026-10-18)

- **Decision (the user, option 1).** New `RunWithOutcomeAsync` / `ContinueWithOutcomeAsync` / `ResumeWithOutcomeAsync`
  return `AgentRunResult(Conversation, Outcome, Failure, StopReason, Detail)`, with an `IsCompleted` property.
  `RunAsync` and the other two old methods keep their names and forward with `.Conversation`, since DraCode writes
  `var messages = await Agent.RunAsync(...)`. No caller breaks.
- **Outcomes.**
  - `end_turn` → Completed; `error` → LlmFailed, with the classified `LlmFailure` from TASK-531.
  - NotConfigured → NotConfigured; `tool_use` on the last iteration → MaxIterationsReached.
  - Beyond the brief, because each is its own terminal path: `max_tokens` → Truncated; `tool_use` without a tool call →
    MissingToolCall; a hook's `Stop` → StoppedByHook.
  - Anything else → UnexpectedStop, with `StopReason`.
- **Decided in one place.** `HandleResponse`, `HandleToolUse` and the hook stop now return the result in place of
  `bool?`, so the outcome is never re-derived.
- **Unexpected stop.** No text is injected, whatever `Verbose` is: injecting it would change what existing callers get
  back, so the reason travels on the result.
- **Small visible change.** With `Verbose` on, a `max_tokens` stop warns "The model's output was cut off by its token
  limit" instead of "Unexpected stop reason: max_tokens". The conversation is unaffected. Cancellation still throws.
- **Tests.** `Task660RunOutcomeTests`, 12: one per outcome (unexpected stop with `Verbose` off and on), Continue and
  Resume, a pin that `RunAsync` returns the same conversation as before, and cancellation. The API is new; mutations
  instead: `error` reported as Completed fails 2, max iterations reported as UnexpectedStop fails 1.
- **Regression.** AI.Agents 82, AI.Providers 246, AI 75. Sandbox builds.
- **Docs and spec.** The `llm-provider-and-agents` spec ("Stop reasons drive loop termination") gains an outcome table
  and two scenarios. The Birko.AI CLAUDE.md and README, and `docs/ai.md`, are updated.
