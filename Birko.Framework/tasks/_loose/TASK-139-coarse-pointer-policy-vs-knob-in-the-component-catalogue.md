---
id: TASK-139
# parent deliberately null: a decision ticket living in _loose/. Declaring a parent while sitting here
# makes it render twice on the dashboard. Related to EPIC-016 in substance; see the body. Same
# convention as TASK-059, TASK-106 and TASK-127.
parent: null
feature: null
status: todo
priority: P2
assignee: human
created: 2026-08-04
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Decide whether a `pointer: coarse` rule inside a `b-*` component is policy or a knob

> **Numbering note.** This is *this repo's* TASK-139. Reps.Web has its own TASK-139 and TASK-149 about the
> same component (the origin report and the touch-floor task), and the `b-segmented` comments cite those.
> Task ids are per-repo in this polyrepo — see CLAUDE.md § "Task tracking" — so the collision is expected;
> when citing across repos, name the repo.

**This is a decision, not an implementation.** Two components in the same catalogue now answer the same
question in opposite directions, each with its reasoning written down, and neither reasoning cites the
other. Filed in `_loose` alongside [[TASK-106]] because it is a framework-wide API-surface convention
spanning the whole `b-*` catalogue, not work on any one component.

## The contradiction

`b-button` **refuses** the media query, in a comment that names the cost
(`Birko.Web.Components/src/inputs/b-button.ts:61-65`):

> Deliberately NOT a `pointer: coarse` media query in here: that would re-render every existing consumer
> without opt-in (a desktop back-office on a touch-capable laptop reports coarse), and it makes a
> component's size depend on the input device rather than on its design. The knob belongs to the
> framework, the policy to the app.

It ships `--b-button-padding-y` / `-x` instead, and the consumer writes the query.

`b-segmented` **applies** it (`src/inputs/b-segmented.ts`, the touch-floor block), and exposes **no knob at
all** — no `size` attribute, no `--b-segmented-*` custom property, no `::part`. A consumer who does not want
the inflation can only re-point `--b-control-min-height-lg` or `--b-text-base`, which are global
form-control tokens shared with `b-input` / `b-select` / `b-textarea` — i.e. precisely the anti-pattern
`b-button`'s note was written to prevent.

So the catalogue currently holds a stated policy and a live counter-example, and the counter-example is the
one with no escape hatch.

## Why it is not obviously wrong

The `b-segmented` decision has a real argument behind it, which is why this is a decision ticket and not a
bug: a tap target that is 43% of the minimum is a **defect**, not a preference, and a knob nobody turns
protects nobody — the same reasoning that made the tenant header/claim guard secure-by-default
(see CLAUDE.md § "X-Tenant-Id must agree with the JWT tenant claim"). A consumer cannot be expected to
discover, per component, that they must opt in to being usable on a phone.

The catalogue also already has coarse-pointer rules that nobody disputes, and they suggest where the line
might fall: `shared-styles.css` bumps input font-size to 16px, and `b-date-picker` drops the UA appearance.
Both **fix a platform defect** (iOS focus-zoom; iOS intrinsic control width). Neither sets a *size policy*.
That may be the distinction worth writing down — repair is the component's business, sizing is the app's.

## Why it needs deciding rather than leaving

`b-segmented` is not a leaf component. `Birko.Web.Shell`'s `base-crud-page` renders it as a **filter-row
chip** (`src/pages/base-crud-page.ts:326,460`), so every Shell-based back-office inherits the rule. Measured
on the shipped bundle, a pill goes from 19px tall / 23px group to 44px / 48px group, with the label from
11.375px to 14px. On a dense filter toolbar that is a visible row-height change that no consumer asked for
and none can decline.

The blast radius is **narrower than `b-button`'s note implies**, and that is worth recording because the
note overstates it: a touch-capable laptop *with a mouse* reports `pointer: fine` — the primary pointer is
what the query tests. So this reaches iPad Safari and Windows tablet mode, not every touchscreen laptop. On
those, arguably, the inflation is *correct*. The question is whether "arguably correct for the device" is
enough to justify a component resizing itself with no opt-out.

## Options

1. **Knob + coarse default (recommended).** Add `--b-segmented-min-height` / `--b-segmented-min-width` /
   `--b-segmented-font-size`, defaulting to the current floor, and keep the media query pointing at them.
   The defect stays fixed by default; a dense back-office can decline in one declaration. Costs three
   custom properties and reconciles both notes.
2. **Follow `b-button`.** Drop the media query, ship the knobs, let the app opt in. Consistent, and
   re-opens the defect for every consumer that does not know to opt in — including Reps, whose four
   surfaces were the origin report.
3. **Make `b-segmented` the convention and revisit `b-button`.** Write down "components floor their own
   tap targets under a coarse pointer" and audit the catalogue against it. Largest scope; would also
   re-open `b-button`'s `type` / padding decisions, which have their own recorded call sites.

## Not in scope

The token defect the floor works around — `--b-control-min-height-lg: 2.75rem` resolves to **38.5px**, not
44px, because `reset.css` sets `html { font-size: var(--b-text-base) }` = 14px, so the whole `rem` scale
resolves against a 14px root. Every `max(token, 44px)` in the catalogue exists because of it. That is its
own ticket and is referenced from the `b-segmented` comment; it is **not** blocking this decision, because
the floor has to hold either way.

## Acceptance

- [ ] One option chosen, with the reasoning recorded here — including why the rejected ones were rejected,
      so this is not re-proposed from first principles (the failure mode CLAUDE.md names for `b-button`'s
      `type` default).
- [ ] The chosen convention written into `Birko.Web.Components/CLAUDE.md` § Conventions, so it applies to
      the next component rather than to these two.
- [ ] `b-button`'s and `b-segmented`'s comments reconciled — whichever way it goes, they must not keep
      contradicting each other with neither citing the other.
- [ ] If option 1 or 3: the knobs added and the framework-side coarse-pointer checks in
      `Birko.Web.Playground/device-fix-check.mjs` (§ 6) extended to cover the override path, not just the
      default.

## Provenance

Found while checking whether the `b-segmented` touch floor had used absolute instead of relative sizing
(2026-08-04). It had not — both floors are inside `max()`, so they scale up with the user's font size and
only ever raise, which is the right shape. The width half of that floor was missing and is fixed
separately; this ticket is the policy question that fix could not answer on its own.
