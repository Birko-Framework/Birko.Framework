---
id: TASK-203
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-11
completed: 2026-08-11
depends-on: []
blocks: []
findings: []
pr: 0914ca1 (Birko.Web.Core, landed 2026-08-02)
github-issue: null
jira-key: null
---

# "nothing recorded" and "never synced" both read as `[]`

## Context

**Tracking backfill.** `0914ca1` landed in `Birko.Web.Core` on **2026-08-02** with no aggregator commit and
no mention in either repo's CLAUDE.md — the only one of the recent Core commits documented *nowhere*. Found
by the same sibling-log diff as [[TASK-202]].

`readAllThrough` returns `T[]`, falling back to `mirror.readAll()`. So a device that has **never synced** and
an account that genuinely **has nothing** both answer `[]`, and any surface that says something to the user
about emptiness then says the wrong thing to one of them. *"You have logged nothing"* and *"this device has
never connected"* are opposite claims and only one screen can be right.

Measured in Reps at the time: **seven** reads had hand-rolled the distinction and two more were added the
same week — one because the collapsed `[]` fed a warning about what a destructive action would delete, and
the same `[]` left a button enabled that could do nothing. The shape is not merely repeated, it is
repeatedly *forgotten*, and each omission surfaces only after it has cost something.

## Approach

`readAllClassifiedThrough` returns a discriminated state (`unavailable` / `loaded` + `source`) rather than an
array, with `createListMirror` shipping the read and `peekList` added during the migration for a second
reader that wants the cached whole as an offline fallback.

**Why `readAllThrough` could not simply be given a better return type**, since it looks like an oversight: an
entity-keyed mirror has nowhere to record that a fetch succeeded and returned nothing — an empty store is
indistinguishable from an absent one. The fix is a different **cache shape**, not a different signature: one
wrapper row under a fixed key. `createListMirror` ships the read for exactly that reason — a caller that had
to know about the wrapper row would be a caller who could get it wrong.

## Acceptance criteria

- [x] Never-synced reads as `unavailable`, not as empty
- [x] A synced-but-empty list stays `loaded` offline
- [x] `peekList` distinguishes synced-empty (`[]`) from never-synced (`undefined`)
- [x] A server error or thrown fetch with a cached row falls back to the mirror, not to `unavailable`
- [x] A mirror on a different key does not see the default key's row

## Outcome

Landed 2026-08-02. Already covered by Playground `backport-smoke` (the `createListMirror` /
`readAllClassifiedThrough` / `peekList` block), which is why this backfill adds no tests — the coverage
shipped with the commit; only the tracking was missing.

- **A polyrepo fix that stops at two commits looks finished.** This one stopped at *one*: production code
  with playground coverage, and nothing in the aggregator. No compiler and no test demands the third commit,
  so the only reliable way to notice is diffing sibling `git log` against this repo's HEAD — now done twice
  and productive both times.

## Out of scope

Migrating Reps' remaining hand-rolled distinctions to the new read — a consumer decision.
