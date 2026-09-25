---
id: TASK-492
parent: EPIC-001
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-09-25
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Playground smoke: `b-multi-select` renders its labels from `bwc.multiSelect.*`, and the smoke can fail

## Context

Split from [[TASK-491]] at its close (2026-09-25) — its last criterion, not met there. TASK-491 made
`b-multi-select` resolve *Search...*, *No matches*, *Remove {option}*, *Options* and the create row through
`t()` (Web `fea5338`). It was verified by a one-off headless Playwright run against a consumer's build
(Symbio's admin, sk vs en) and by a person clicking a *Tags* field — neither is a check that lives in the
framework and runs again.

## Acceptance criteria

- [ ] A Consumers/Birko.Web.Playground smoke loads a locale bundle that defines `bwc.multiSelect.*` and
      `bwc.select.create`, renders a `b-form` `multi-select` field, and asserts each of the five strings
      comes from the bundle — with **no** `label-*` attribute set.
- [ ] Also asserts an explicit `label-*` attribute still wins over the key.
- [ ] Proven able to fail: reverting `_text()` to the old `this.attr(…, 'English')` reddens it.

## Out of scope

- The create *behaviour* — [[TASK-490]].

## Human test plan

N/A — the smoke is the instrument; a person re-clicking adds nothing TASK-491's sign-off did not.
