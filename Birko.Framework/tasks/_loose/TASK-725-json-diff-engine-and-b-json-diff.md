---
id: TASK-725
parent: null
feature: null
status: verify  # verified headlessly 2026-10-22; the human test plan is left
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A generic JSON diff in Birko.Web.Core and a `b-json-diff` component

## Context

Requested by the ClientApi.CSharp session (FinStat web API tester, `Tester/WebFinstatApiTester`), which
will send one API request to two environments (e.g. production vs localhost) and diff the two raw JSON
responses. Design source: Outline doc e9938a9b ("API klienti a testery – podpora v2 a porovnanie
prostredí", sections "Porovnanie prostredí" and "Čo patrí do Birko.Framework"). Nothing FinStat-specific
goes into Birko — the tester supplies its own ignore and normalize rules.

## Acceptance criteria

- [x] Birko.Web.Core: a pure, DOM-free `diffJson(a, b, options)` returning changes keyed by path
      (added / removed / changed with old and new value), a tree shape for rendering, and summary counts
- [x] Options: ignored paths with wildcards (`*.Url`, `Items[*].Ref`, `**` for any depth), per-path value
      normalizers, arrays by index by default, or matched by a key, or unordered for chosen paths
- [x] Birko.Web.Components: `b-json-diff` — tree of changes with added / removed / changed highlighting,
      "only changes" toggle, side-by-side view of both bodies, settable A/B labels; i18n, tokens, a11y
- [x] Playground smoke suite for both; gallery entry; docs (Core and Components CLAUDE.md, README, API.md)

## Out of scope

- The C# JSON diff, an Avalonia view, environment profiles (stay in the tester), XML

## Human test plan

- [ ] In the Birko.Web playground gallery, open "JSON diff": toggle Tree / Side by side and Only changes,
      expand and collapse, switch theme to dark and check the added / removed / changed colours read
- [ ] In the FinStat web tester, compare one request across two environments once it is wired

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, **not pushed or tagged** (a push needs a version and a tag, per the Web
repo's rules). The tester consumes the source through `BIRKO_SRC`, so it can wire this up before a release.

- **`diffJson(a, b, options)`** (`birko-web-core`, also at `birko-web-core/diff`). It returns `{ equal, changes, tree,
  summary }`. Changes are `{ kind, path, segments, oldValue?, newValue?, oldPath? }`.
  - `ignore`, `normalize: [{ path, normalize }]`, `arrays: [{ path, match: 'index' | 'unordered', key }]`.
  - A pattern matches the **whole** path, so `*.Url` is one level down and `**.Url` is any depth. A malformed pattern
    throws, because an ignore rule that matched nothing would read as a real difference.
  - Below a paired array, a node sits at its side-B index and `oldSegments` / `oldPath` give its side-A position.
    The side-by-side view needs this to mark the right item.
  - Also exported: `matchJsonPath` and `formatJsonPath`.
- **`b-json-diff`**:
  - Methods: `setData(a, b)` (objects or strings), `setOptions`, `getResult`, `expandAll`, `collapseAll`.
  - Attributes: `label-a`, `label-b`, `view` (tree | split), `only-changes`, `size`, `max-height`.
  - Events: `diff-computed`, `view-change`, `only-changes-change`, `toggle`.
  - The tree uses `<ins>` / `<del>` with the status as screen-reader text. The side-by-side view stacks below 48rem.
- **Gate.** Playground `json-diff-smoke` 74/74, all other suites green, entry-points 409/409, subpath and both
  a11y checks pass, `tsc` clean on Core, Components and the Playground.
  - Mutation: dropping `oldSegments`, or ignoring nothing on one-sided nodes, fails 3 checks.
  - Screenshots checked in light and dark, at 1100px and 390px.
