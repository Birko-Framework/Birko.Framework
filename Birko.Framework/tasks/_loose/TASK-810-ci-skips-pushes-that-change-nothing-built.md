---
id: TASK-810
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
priority: P2
assignee: ai
created: 2026-11-03
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# CI skips pushes that change nothing the build reads

## Context

Asked by the FlowerFur storefront session at the user's request (its TASK-196, BirkoWorks/FlowerFurStudio#321), taken
on by the user ("take this tasks first"). GitHub Actions minutes ran out once, at the end of October. In this repo
`build-and-test.yml` (whole solution, every offline suite, the Sandbox) ran on every push and PR, including pushes of
task files and docs only, and `wiki-snippets.yml`'s `Birko.*/**` filter matches `Birko.Framework/**`, so it did too.
The other five workflows were already path-filtered or scheduled.

## Measured (2026-11-03)

- No build step, test project or the Sandbox reads `Birko.Framework/tasks/` or `Birko.Framework/docs/`.
- Of the `.md` files, one is read by a test: `Birko.Xaml.Avalonia/PARITY.md` (`WebCheckout.ParityListPath`). The
  `README.md` strings in `GitServiceTests` are files the test writes into a temp repo; `<None Include=README.md>`
  in two projitems compiles nothing.
- `Birko.Framework/tools/` is not excludable: `AuditCommon` is in the `.slnx` and tested.
- `wiki-snippets` compiles the wiki (a separate repo) against `Birko.*` sources; no `.md` here is read.
- No branch protection, no PRs in use (commits go to main), and no tag or package is produced from this repo, so no
  release depends on every main commit being built.

## Acceptance criteria

- [x] `build-and-test.yml`: push and pull_request skip `Birko.Framework/tasks/**`, `Birko.Framework/docs/**` and
      `**/*.md`, PARITY.md re-included. Written as `paths:` (`'**'`, the negations, the re-include) because
      `paths-ignore` cannot re-include a file; spelled out on both triggers, no anchor.
- [x] `wiki-snippets.yml`: the same three exclusions after `Birko.*/**`.
- [x] Workflows themselves still trigger (`.github/**` is not excluded).
- [x] Recorded in CLAUDE.md § Commands and in the workflow's header comment.
- [ ] Verified on GitHub: the next push touching only task files or docs starts no `build-and-test` / `wiki-snippets`
      run, and a push with code still starts one.

## Out of scope

- Birko.Web: its only workflow (`token-parity.yml`) is already path-filtered.
- Skipping CI for code pushes: the user decides per push (asked at "push"; `[skip ci]` on the head commit).

## Human test plan

After the next push: `gh run list --limit 5` shows no `build-and-test` run for a docs/tasks-only push.
