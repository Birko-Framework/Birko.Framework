---
id: TASK-522
parent: STORY-058
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-07
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Optional "no token needed when listening on loopback only" for Birko.Security.AspNetCore

## Context

DraCode's daemon mode lets local tools call the server without a token when every bound address is loopback
(`DraCode.KoboldLair.Server/Auth/DaemonLoopback.cs`, 65 lines): off by default, fails safe to enforcing when the bindings are
unknown, and gives the request a principal with a wildcard permission.

Adopted in the consumer by DraCode TASK-111 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [ ] An option on `AddBirkoSecurity` (off by default) with the same fail-safe rules
- [ ] Tests: off never bypasses; all-loopback bypasses; any non-loopback or unknown binding enforces

## Out of scope

- Any other bypass rule

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
