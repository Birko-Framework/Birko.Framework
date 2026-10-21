---
id: TASK-715
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

- [ ] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [ ] Each reproduced defect fixed, with a regression test that fails without the fix.
- [ ] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Implementation plan

_Populated by `/tasks plan TASK-715` — leave empty until then._
