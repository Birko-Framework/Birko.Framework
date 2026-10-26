---
id: TASK-769
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-26
depends-on: []
blocks: [TASK-770, TASK-771]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Shared diff test vectors for `diffJson` / `xmlToTree`, run by the web engine

## Context

FEATURE-020 D4 + D5. The web diff engine (`Birko.Web.Core/src/diff/json-diff.ts`, `xml-tree.ts`) has **no tests at all**,
and a C# port is about to be written (TASK-770, TASK-771). One vectors file, owned by the web repo (the engine of record),
is run by both suites so the two engines cannot drift. This lands in the **Birko\Web** git repo, not Birko\Framework —
one commit there, with this task file's update committed here.

Function-valued options (`normalize.normalize`, `arrays.key` as a function) cannot be data. The vectors express them as
a small closed vocabulary that both runners map to a function (e.g. `{ "replace": "regex", "with": "…" }` for the
host-to-`{sk}` normalize the tester uses); anything outside it is tested per-suite, not in the vectors.

## Acceptance criteria

- [ ] `Birko.Web.Core/src/diff/diff-vectors.json`: each case has `name`, `a`, `b`, optional `options`, and the expected
      `equal`, `summary`, `changes` (kind, path, oldPath, oldValue, newValue) and the tree statuses by path.
      XML cases carry `xmlA` / `xmlB` (+ `xmlOptions`) and the expected `xmlToTree` output as well.
- [ ] Coverage: every change kind; every tree status (`equal/added/removed/changed/modified/ignored`); type change
      object↔array↔value; `ignore`; chained `normalize`; arrays by index, `unordered` (incl. duplicates) and `key`
      (incl. items missing the key); globs `*`, `[*]`, `**` (incl. zero segments) and quoted keys (`["a.b"]`); root
      path `''`; nulls vs missing; XML repeated elements → list, single element, `xsi:nil` → null, attributes, text.
- [ ] A Node runner (`node --test`, TypeScript via the repo's existing esbuild/tsx — no new framework) runs every
      vector against the web engine; all pass. Any vector the web engine fails is a web bug: file it, do not
      bend the vector.
- [ ] Documented in `Birko.Web.Core/README.md` (diff section): what the file is, who reads it (TASK-770's C# suite),
      and that changing engine behaviour means changing a vector.

## Out of scope

- The C# side (TASK-770, TASK-771).

## Human test plan

N/A — fully covered by automated tests (the runner is the deliverable).

## Implementation plan

## Review log
