---
id: TASK-487
parent: null
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-09-24
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `Birko.Web.*` is consumed by source path, so no build records which commit it carries

Found while closing [[TASK-486]], whose `## Out of scope` names this as a real but larger gap.

## Context

Symbio does not depend on a versioned `birko-web-components`. Its `build.js` (esbuild `alias`,
lines ~58–61) points `birko-web-components` at `${BIRKO_SRC}/Birko.Web.Components/src/index.ts`, and
`tsconfig.json` mirrors the alias for the editor. The Playground, Presenter and the other web
consumers do the same.

Two consequences, both measured while fixing TASK-486:

- **A local edit reaches a consumer's bundle before it is committed or pushed anywhere.** The fix was
  live in the Playground the moment it was saved; the only thing that made that acceptable was
  running the suite deliberately.
- **Nothing records which upstream commit a consumer build carries.** TASK-486 could name a commit
  (`Birko.Framework/Birko.Web` `7e26fba`) only because the framework monorepo happens to be one
  repo; a consumer's built `app.js` still cannot be traced to one.

⚠ **Not the same item as [[TASK-468]].** That one is `BIRKO_SRC` naming two different checkouts to
MSBuild and esbuild — a path-resolution defect. This is the absence of any *version* pin to resolve
*to*.

## Acceptance criteria

- [ ] Decide the mechanism, and record the decision: published `@birko/web-*` packages with semver,
      a pinned git submodule/tag per consumer, or aliases pinned to a declared commit — naming what
      each costs (a publish pipeline, a submodule step in every consumer, or drift).
- [ ] Implement the chosen mechanism for at least one real consumer (Symbio or the Playground).
- [ ] A consumer build can name the upstream commit/version it carries, without reading a lockfile
      the alias bypasses.
- [ ] The migration is reversible for one release: consumers can pin and stay on the current
      source-path behaviour until they move.

## Out of scope

- The TASK-486 defect itself (fixed).
- Publishing infrastructure for the .NET `Birko.*` projects; this is the `Birko.Web.*` TS packages.