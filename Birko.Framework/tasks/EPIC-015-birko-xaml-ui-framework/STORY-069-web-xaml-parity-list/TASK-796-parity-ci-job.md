---
id: TASK-796
parent: STORY-069
feature: FEATURE-021
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] The workflow runs the parity tests with the web checkout and `BIRKO_REQUIRE_LIVE=1`, on the paths above and by hand.
- [x] A run on GitHub is green, and its log shows the parity tests ran (not skipped).
- [x] The list's header says how to re-run the job after a web-side change.

## Out of scope

- Triggering the job from the Birko.Web repo.

## Human test plan

- [x] 1. Start the workflow by hand on GitHub: it passes, and the test step's log shows the parity tests ran rather than skipped.
  - **Passed 2026-10-30:** dispatched by the agent with `gh workflow run` (run 41059917547): success, `Total tests: 34, Passed: 34`, both `Every_web_piece_has_exactly_one_valid_row` cases passed, no `SKIPPED` line.

## Implementation plan

Modelled on `diff-parity.yml`, as the Context says; the filter is `FullyQualifiedName~Parity`, so the counterpart check
runs here too.

## Review log

- 2026-10-30 — First looked at why `diff-parity` was red on main (the workflow this one copies): its only run, on
  10-27, failed all six vector tests with "Birko\Web checkout … not found but BIRKO_REQUIRE_LIVE is set". The
  vectors file is on Birko.Web's main now; re-run by hand (41058957049) it passed 28/28. A run before the web commit
  reached the remote, not a defect in the workflow, so nothing changed in it.
- `.github/workflows/xaml-web-parity.yml`: both checkouts, `BIRKO_SRC` + `BIRKO_REQUIRE_LIVE=1`, the parity tests
  only; push / PR on Birko.Xaml.*, the Avalonia tests and itself; `workflow_dispatch`. `PARITY.md` says that a web-side
  change does not trigger it and how to run it.
- Pushed with the rest of main (be3f89a4..5f105669, the maintainer's go-ahead). Runs: push 41059905611 and dispatch
  41059917547, both success, 34/34 parity tests, the two live sections passing (not skipped).
- Gate (inline): conventions — mirrors the existing parity workflow; intent — all three criteria; correctness and
  security — the token falls back to `github.token` as `diff-parity` does, the job writes nothing. Done.
