---
id: TASK-602
parent: STORY-038
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: unassigned
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-040]
pr: null
github-issue: null
jira-key: null
---

# No edit form guards unsaved changes, so any navigation silently discards typed input

> **Moved from WorkoutTracker TASK-151 on 2026-10-14.** It was filed there as Birko.Web work, where the framework's pick never saw it. Every TASK / STORY / FEATURE id in the body below is **WorkoutTracker's**. There, TASK-151 stays open as the Reps-side check, blocked on this task.


> **P3 → P2, 2026-08-08 (roadmap re-rank).** Silent data loss across twelve edit forms, on a bottom-nav app
> where a mis-tap is one thumb away. Losing typed input is not polish.

## Where this lives, and why the two halves point at different places

`parent: STORY-009` (frontend Birko.Web backports) but `feature: FEATURE-002` — the project's own rule: **the
`feature:` says whose behaviour changes, the parent story says where the code lands.** A user would notice this on
the plan-builder and dictionary forms, which are FEATURE-002's; the mechanism is a leave-guard over the framework's
hash `Router`, which is STORY-009's.

Filed under STORY-004 first, which `/tasks triage` caught on 2026-08-04: that story is `done`, and a `done` story
cannot carry open work without lying about itself. Re-homed rather than reopening the story.

## Context

**Found 2026-08-04 while arguing about TASK-140's navigation**, not from a report. The owner asked whether the new
Plans segment pills should also appear on the exercise editor; the answer turned on whether leaving a half-filled
form costs anything, so it got checked: **nothing in `Reps.Web/src` references `beforeunload`, a dirty flag, or any
leave-confirmation.** Twelve pages carry an edit form (`plan-edit`, `phase-edit`, `variant-edit`, `template-edit`,
`workout-exercise-edit`, `exercise-edit`, …) and none of them notices unsaved work.

**This is live today, not only a consequence of that proposal.** Type half an exercise into `#/library/new` and then
tap the bottom nav, or `Back`, or the browser/PWA back gesture, and the input is gone with no prompt. It is quiet
because the current exits are deliberate — `Back` reads as "I am leaving" and the bottom nav is a long thumb-move
from a form field — so nobody has hit it hard enough to file it.

**P3, and the reasoning matters more than the number.** Nothing is corrupted and nothing is lost that the user had
committed; the cost is retyping. It is filed because it is the **unblock condition for a change the owner may want**:
pills (or any lateral navigation) on a detail screen is a data-loss trap while this gap exists, and that trade was
made explicit rather than left as an unexplained "no" — see FEATURE-002's D12 clarification.

## Change

A shared dirty-check, not twelve of them. Sketch, deliberately not a design:

- The pages already read their fields into a form model on save. The cheapest honest signal is comparing that model
  to the values the page mounted with — no per-field bookkeeping, and it correctly reports "not dirty" when a user
  types something and then types it back.
- One confirm on leave, reusing `confirmDelete`'s dialog family (`birko-web-components/dialogs`) rather than
  `window.confirm` — TASK-062 replaced native dialogs for exactly this reason and a new one must not reintroduce them.
- The hash router is the interesting part: a leave-guard has to intercept a route change the app performs itself,
  which is a framework concern (`Router`) rather than a per-page one, so this likely lands as a backport under
  STORY-009 with the pages opting in.
- `beforeunload` covers the tab-close / reload case and is a different, cruder mechanism (no custom text, fires on
  every reload). Worth deciding explicitly whether it is in scope, rather than adding it because it is easy.

## Acceptance criteria

- [ ] Typing into any edit form and then navigating away (bottom nav, `Back`, or an in-app link) prompts once, and
      staying keeps the typed values intact.
- [ ] A form the user has *not* touched navigates away with no prompt — including one where a value was typed and
      then reverted by hand.
- [ ] Saving, then navigating, never prompts (the save clears the baseline).
- [ ] The prompt is the app's own dialog, not `window.confirm` (TASK-062 removed those on purpose).
- [ ] Delete-and-leave still works: the confirm-delete flow must not stack two dialogs.
- [ ] Every one of the twelve edit pages is covered by the shared mechanism, or its exemption is recorded — a guard
      on some forms and not others is worse than none, because the user learns to trust it.
- [ ] E2E: one case per mechanism (in-app navigation, and reload if it is in scope). Verified red first.

## Human test plan

- [ ] On the device, start a new exercise, type a name, tap `Dnes` — confirm the prompt appears and that staying
      keeps the name.
- [ ] Repeat with the iOS back-swipe, which is the gesture most likely to bypass an in-app guard.
- [ ] Save an exercise, then leave — no prompt.
- [ ] Delete an exercise from its editor — one dialog, not two.
