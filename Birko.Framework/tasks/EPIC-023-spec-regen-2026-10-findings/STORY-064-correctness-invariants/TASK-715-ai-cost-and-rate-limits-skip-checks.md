---
id: TASK-715
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
findings: [SH-T696-10]
pr: null
github-issue: null
jira-key: null
---

# AI: cost budgets and rate limits skip their checks

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `CostTrackingService` returns early on a daily warning, so an exceeded monthly or project budget is never checked. `TrackedLlmProvider` counts a request against the rate limiter only when the response carries usage, and the OpenAI-compatible providers send none, so their request-per-minute limits never apply. `AgentRegistration` mutates the caller's config dictionary.

Evidence: Birko.AI/.../CostTrackingService.cs:96; TrackedLlmProvider.cs:168; AgentRegistration.cs:119.

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

`tests/Birko.AI.Resilience.Tests/Task715BudgetAndRateLimitTests.cs` and
`tests/Birko.AI.Agents.Tests/Task715AgentRegistrationTests.cs` against the shipped code.

| Claim | Result |
|---|---|
| A daily warning returns early, so an exceeded monthly or project budget is never checked | **reproduced:** $9 spent against a daily $10 (warning) and a monthly $5 or a project $3 answered within budget |
| A request counts against the rate limiter only when the response carries usage; the OpenAI-compatible providers send none | **reproduced:** with 1 request per minute, two usage-less calls both reached the provider. `OpenAiCompatibleProviderBase` sets no `Usage` (parsing it is TASK-716) |
| `AgentRegistration` mutates the caller's config | **reproduced:** the caller's dictionary gained `useCodingEndpoint = "true"` |

## Implementation plan

`CheckBudgetAsync` assesses every scope and answers the first exceeded one, else the first warning. `TrackedLlmProvider`
counts every request it made (a failed one too) and adds a stream's tokens at its end through a new
`ProviderRateLimiter.RecordTokens`, so a streamed request is not counted twice; cost still needs usage.
`AgentRegistration.Create` works on a copy of the config with the caller's comparer.

## Built (2026-11-04)

6 tests: the 4 measurements, all failing on the old code; one pinning that the first warning is still reported when nothing is exceeded, and one for `RecordTokens`. AI.Agents 83, AI.Providers 246,
AI.Resilience 18, AI.Resilience.Data 24, AI 75 green. Spec `llm-provider-and-agents.md` updated (scoped; `generated-at`
left).
