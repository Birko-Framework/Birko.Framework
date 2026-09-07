---
id: TASK-301
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P3
assignee: ai
created: 2026-09-07
depends-on: []
blocks: []
related: [TASK-269]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `Birko.EventBus.Outbox.SQL` is in the build and in no documentation index

## Context

Surfaced by the close gate on [[TASK-269]], from `verify-birko-conventions` check 7b's **full-repo drift
sweep** — the one that deliberately looks past the current diff so a pre-existing gap cannot hide behind
a clean change. It is unrelated to TASK-269 and was spawned rather than fixed there.

```
UNDOCUMENTED: Birko.EventBus.Outbox.SQL
```

The project builds and is registered, so nothing fails; it simply cannot be found by anyone reading the
docs. `CLAUDE-maintenance.md` § *Documentation Index Registration* records that this is the exact gap
that let `Birko.EventBus.Tenant` ship undocumented, which is why the check exists at all.

## What to do

Add it to all three index locations, matching how its siblings are described:

1. **`README.md`** — a table row in the project catalog.
2. **`CLAUDE-projects.md`** — a bullet in the EventBus group.
3. **`docs/event-bus.md`** — a section on the topic page for its area.

Then re-run the sweep and confirm it reports nothing:

```bash
cat Birko.Framework/README.md Birko.Framework/CLAUDE-projects.md Birko.Framework/CLAUDE.md Birko.Framework/docs/*.md > /tmp/alldocs.txt
for d in Birko.*/; do d="${d%/}"
  case "$d" in *.Tests|*.ViewModel|*.Views) continue;; esac
  { ls "$d"/*.shproj >/dev/null 2>&1 || ls "$d"/*.projitems >/dev/null 2>&1; } || continue
  grep -qF "$d" /tmp/alldocs.txt || echo "UNDOCUMENTED: $d"
done
```

## Acceptance criteria

- [ ] Present in `README.md`, `CLAUDE-projects.md` and `docs/event-bus.md`.
- [ ] The drift sweep reports zero `UNDOCUMENTED:` lines.
- [ ] The description says what the project actually does — read its `CLAUDE.md` and its public types
      rather than inferring from the name. A wrong index entry is worse than a missing one, because the
      sweep will then report clean forever.

## Out of scope

- Any code change. This is documentation only; the project itself is registered and building.
- The `## Recent Updates` entry — a doc-index backfill is not an architectural change.

## Human test plan

- [ ] None needed; the sweep is the verification.
