---
id: TASK-521
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

# Web (authorization-code) sign-in helpers for external OAuth providers, GitHub first

## Context

`Birko.Communication.OAuth.Providers` covers GitHub's device flow only. DraCode's GitHub web sign-in needed a short-lived
single-use `state` store (`OAuthStateStore`, 51 lines), a DI-friendly code exchanger over `OAuthClient`
(`GitHubTokenExchanger`, 57) and a user-info client returning id/login/name/email (`GitHubUserInfoClient`, 62). None of it is
DraCode-specific.

Adopted in the consumer by DraCode TASK-111 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [ ] A state store (create, single-use consume, expiry) in `Birko.Communication.OAuth`
- [ ] GitHub provider: authorization-code settings and a user-info call returning a typed profile
- [ ] An injectable exchanger interface, so apps can test callbacks without github.com
- [ ] Tests

## Out of scope

- An app's allowlist and user provisioning (policy, stays in apps)

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
