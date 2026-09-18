---
id: TASK-032
feature: FEATURE-011
parent: STORY-022
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Data.*.ViewModel CRUD tests

## Context

CRUD pattern tests on the `Birko.Data.*.ViewModel` repositories. Confirms the abstract `MapToModel` + lazy-init template-method works correctly across platforms.

## Acceptance criteria

- [ ] Test project(s) exist for ViewModel repositories
- [ ] CRUD round-trip per platform (at least SQL + MongoDB + ElasticSearch + RavenDB)
- [ ] `MapToModel` override exercised
- [ ] Paging + filtering exercised
- [ ] FluentAssertions assertions
- [ ] Wired into `Birko.Framework.slnx`

## Out of scope

- Performance benchmarks
