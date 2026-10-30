---
id: FEATURE-021
created: 2026-10-30
owner: both
# status — one of: idea, review (built, sign-off pending), done, dropped, superseded
status: idea
---

# Birko.Web ↔ Birko.Xaml — a parity list, kept honest by a test

> Stakeholder-readable. A project manager or end user should understand the problem and the proposed shape without reading any code.

## Problem

Birko has two UI libraries that are meant to match: the web one (Birko.Web — its components and its application shell)
and the desktop one (Birko.Xaml, on Avalonia). Nothing records which web pieces the desktop has. Asked on 2026-10-30
which web components are missing on the desktop, the answer had to be worked out by hand, component by component. That
survey found four kinds of answer:

- **built** as a Birko desktop control (for example the command palette, the diff viewer, the ribbon);
- **a desktop built-in control in the Birko look** (for example buttons, check boxes, tabs, the data grid) — nothing to
  port, the toolkit already has it;
- **only a field inside a form** (for example the date-range picker, the multi-select) — usable in a form, not on its own;
- **missing** — either the desktop toolkit has the control but Birko has not styled it yet (the number stepper, the
  colour picker, the carousel), or nothing exists at all (pagination, rating, the sortable list, …).

Today a new web component can ship and nobody notices the desktop now lacks it. Some web pieces should be skipped on
purpose, because the desktop toolkit already covers them or because they make little sense in a desktop app — but a
skip that is not written down cannot be told apart from something forgotten.

## Proposed shape

1. **A parity list**: one row for every web piece, saying where it stands on the desktop — built, a built-in control in
   the Birko look, a form field only, needs a Birko look, missing, or skipped with the reason. It covers the web
   components (one row each) and the web application shell (one row per source file).
2. **A check that keeps the list honest**: it reads the web library and fails when a web piece has no row, when a row
   names a web piece that no longer exists, or when a row claims a desktop control that is not there.
3. The list starts from the 2026-10-30 survey. Nine components are skipped as not needed on the desktop: the guided
   tour, chat, image gallery, segmented control, button group, toolbar, empty state, loading skeleton and the
   "data is stale" banner.

Porting the components that stay "missing" is **not** part of this feature: the list makes them visible, and each port
is decided on its own later.

## Open questions distilled from the grill

| id | question | type | blocked-by | state | claimed-by |
|----|----------|------|------------|-------|------------|
| Q1 | Where does the list live, and in what form? | decision | — | resolved → D2 | — |
| Q2 | What may a row say about a web piece? | decision | Q1 | resolved → D3 | — |
| Q3 | Which web libraries does the list cover — the components only, or the application shell too? | decision | — | resolved → D4 | — |
| Q4 | For the application shell, which has no components, what does one row stand for? | decision | Q3 | resolved → D5 | — |
| Q5 | What makes the check fail? | decision | Q1 | resolved → D6, D7, D8, D9 | — |
| Q6 | What happens when the web library is not checked out next to the framework? | decision | Q5 | resolved → D10 | — |
| Q7 | Which components with no desktop equivalent are skipped rather than left missing? | decision | Q2 | resolved → D11 | — |
| Q8 | Is porting the missing components part of this feature? | decision | Q7 | resolved → D12 | — |
| Q9 | Does the build server run the check with the web library present, so a skipped check never passes as green? | decision | Q6 | resolved → D13 | — |

**Fog** — concerns not yet precise enough to state as a question:

- A change on the web side (a new component) lands in the other repository and does not trigger the framework's build
  job (D13), so the list may go stale until the job is run by hand or the framework side changes.

## Out of scope (initial)

- Porting any component that the list marks missing — each is its own later decision (D12).

## Prototype

Skipped — the deliverable is a list and an automated check; the list itself is what a stakeholder reviews, and the
check's failing and passing runs are the proof.
