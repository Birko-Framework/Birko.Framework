---
id: TASK-796
parent: STORY-069
feature: FEATURE-021
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
priority: P2
assignee: ai
created: 2026-10-30
depends-on: [TASK-793]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# CI job for the parity list, with the web checkout

## Context

FEATURE-021 D13. In `build-and-test.yml` there is no Birko\Web checkout, so the parity test skips and a skip reads as
green. Add a workflow of its own (e.g. `.github/workflows/xaml-web-parity.yml`), modelled on `diff-parity.yml`: check out
the framework to `birko/Framework` and `Birko-Framework/Birko.Web` to `birko/Web` (token `BIRKO_CI_TOKEN || github.token`),
set `BIRKO_SRC` and `BIRKO_REQUIRE_LIVE=1`, and run only the parity tests. Triggers: push/PR on the list, the test, the
Birko.Xaml projects and the workflow file; `workflow_dispatch` for a web-side change, which does not trigger it (the
idea.md Fog note) — say so in the workflow's header comment and in the list.

## Acceptance criteria

- [ ] The workflow runs the parity tests with the web checkout and `BIRKO_REQUIRE_LIVE=1`, on the paths above and by hand.
- [ ] A run on GitHub is green, and its log shows the parity tests ran (not skipped).
- [ ] The list's header says how to re-run the job after a web-side change.

## Out of scope

- Triggering the job from the Birko.Web repo.

## Human test plan

- [ ] 1. Start the workflow by hand on GitHub: it passes, and the test step's log shows the parity tests ran rather than skipped.

## Implementation plan

## Review log
