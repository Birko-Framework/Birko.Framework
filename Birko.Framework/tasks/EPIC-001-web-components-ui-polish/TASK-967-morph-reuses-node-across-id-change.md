---
id: TASK-967
parent: EPIC-001
feature: null
status: verify
priority: P1
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
related: [TASK-962]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A re-render turned one element into another with a different id, keeping its listeners and runtime attributes

## Context

Reported by Symbio, 2026-11-22: its gate test hotel.extra.spec.ts "create room blocks on empty required fields" passed
on Birko.Web v0.4.20 and failed on v0.5.0. Symbio bisected it to TASK-962 (v0.5.0 with only base-component.ts put
back to v0.4.21 passed) and found the mechanism with a listener probe.

The morph pairs children by position. Symbio's rooms page renders one element (an empty-state card) ahead of its two
modals, and after a config is picked, two (filters and the split panel). Every later node shifts by one, so the old
`#batch-modal`, its form and its `#batch-save-btn` were updated in place into `#add-modal`, `#room-form` and
`#add-save-btn`, keeping the batch click listener. The page wires each button once, marked with `data-wired`:

- Before v0.4.22 the morph removed `data-wired`, so the page wired the node again: Create ran both handlers (batch
  first), harmless only because the batch form was empty.
- Since TASK-962 (v0.4.22) the morph keeps an attribute the parent never rendered, so `data-wired` stayed and the
  create handler was never attached: Create ran only the batch handler, and validate() on the room form never ran.

## Change (Birko.Web v0.5.1)

Decided by the user (František Bereň, maintainer), 2026-11-22: replace on id change (over matching children by id,
and over a fix in Symbio only). Where the old and new element at a position both have an `id` and the ids differ, the
morph replaces the node with the new render's instead of updating it in place.

## Acceptance criteria

- [x] After a re-render that shifts elements, each button runs only its own handler, once.
- [x] Playground check red before, green after; Core README and CHANGELOG say what changed.
- [ ] Symbio's failing gate test passes against the fix.

## Evidence

Birko.Web `task/TASK-967` 4a2c741 (v0.5.1), Playground e0fea78:
- backport-smoke, Symbio's page shape (one element becomes two ahead of two modals, buttons wired once with
  `data-wired`), clicking add-save-btn then batch-save-btn: v0.5.0 ran "batch, batch"; v0.5.0 with TASK-962 undone ran
  "batch, add, batch"; the fix runs "add, batch". The shifted form still marks its invalid fields on all three.
- verify 0 failing (backport 484/484; undo-delete-smoke threw once on a toast lookup and passed on the next two runs,
  unrelated timing); keyboard 244/244; rerender-leak PASS; locale-switch 47/47; contextual-ribbon 13/13; a11y-name OK;
  entry-points 433/433; device-fix 247/247; sortable 19/19; tree drag 17/17; escape-in-modal 27/27; Core `tsc` clean.
  All from builds that printed "✓ Built".

## Human test plan

1. Symbio runs hotel.extra.spec.ts "create room blocks on empty required fields" against a Symbio.UI build with
   BIRKO_SRC=C:\Source\Birko\Web-TASK-967: it passes.
2. Optional, in that Symbio build: Hotel → Rooms, pick a hotel config, Add room, click Create with the form empty: the
   required fields show their errors and the modal stays open.
