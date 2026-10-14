---
id: TASK-599
parent: STORY-038
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: unassigned
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-037]
pr: null
github-issue: null
jira-key: null
---

# Every semantic colour-on-tint pairing is below the AA contrast floor, in both themes

> **Moved from WorkoutTracker TASK-123 on 2026-10-14.** It was filed there as Birko.Web work, where the framework's pick never saw it. Every TASK / STORY / FEATURE id in the body below is **WorkoutTracker's**. There, TASK-123 stays open as the Reps-side check, blocked on this task.

> **Re-measured 2026-10-14** (`device-fix-check.mjs`, dark): success 4.05, warning 4.22, danger 3.10, info 3.64, primary 3.13 — unchanged. Framework TASK-562/563 fixed `--b-text-muted` and text on primary fills, not these pairs. The check passes them only because it asserts "stays readable" (~3:1), not AA.


## Context

**Filed 2026-08-02, late.** Recorded on **TASK-113** as *"Recommend a follow-up task… Not filed here because
the value choice affects every consumer and deserves its own decision"* — and then not filed, for a day, on a
task that closed the same day. The reasoning was sound and the conclusion was wrong: a change needing a
decision is a reason to file it **with an open decision**, not a reason to leave it in a paragraph. This is
the same failure as the `[hidden]` guard (TASK-122), and it is the third instance of the pattern found in one
sweep.

### The measurement

`device-fix-check.mjs` in the Playground sweeps all five pairings. Reading
`--b-color-<x>` on `--b-color-<x>-light`, in **dark**:

| | success | warning | danger | info | primary |
|---|---|---|---|---|---|
| contrast | 4.05 | 4.22 | **3.10** | 3.64 | 3.13 |

**Every one is below the 4.5:1 AA floor.** `danger` — the variant most likely to carry something a user must
not miss — is the worst. The light theme is **worse still** for the one pairing measured there: the same
`warning` combination is **2.86:1** on `#fef3c7`.

Two things that follow, and the second is why this is P1 rather than a polish item:

- It is **pre-existing and cross-theme**, not a dark-mode regression. TASK-113's token gap merely hid it in one
  theme; fixing the gap made the badges *readable* without making them *compliant*.
- It is in the **shared component library**, so it is every Birko consumer's defect, not Reps'. Anything
  rendering a semantic badge, tag or pill on its own tint is affected.

### Scope correction already on the record

TASK-113's first paragraph sized this from a **single** measurement (warning at 4.22) and read as "one variant
is marginally short". The sweep that followed showed the whole family fails and that 4.22 is the *best* of it.
**Start from the table above, not from that paragraph** — sizing this as one-variant work is the mistake the
correction exists to prevent.

## Open decision — settle before implementing

The fix shape is known: per-theme **text** colours for the semantic family, mirroring `--b-color-danger-text`,
which already exists and was added for exactly this reason. What is not settled is the values, and that choice
lands on every consumer.

| Option | Effect | Cost |
|---|---|---|
| **Darken the text colour** per theme (add `--b-color-<x>-text` for all five) | Keeps the familiar tints; only the foreground moves. Follows the precedent already set by `danger-text`. | Five new tokens × themes to choose and maintain; the text stops being the same hue family as the tint. |
| **Lighten the tint** (`-light`) instead | Fewer tokens; the existing text colour then passes. | Changes the *look* of every badge in every consumer — a visual change, not just a contrast fix. |
| **Both, per variant** | Best contrast per case. | Most values to choose, least predictable as a system. |

**Recommendation: darken the text**, following `danger-text`. It is the precedent already in the codebase, it
leaves the visual identity of the tints alone, and it moves the thing that is actually failing. But this is a
palette decision with a visual consequence in two consumers, so it should be *chosen*, not assumed.

## Change

1. `Birko.Web.Components` — add the missing `--b-color-<x>-text` tokens per theme (per the decision above),
   and point the semantic badge/tag/pill styles at them.
2. Extend the Playground's `device-fix-check.mjs` sweep to assert the AA floor for all five pairings **in both
   themes**, so this cannot regress silently — it is currently measured but not enforced.
3. Reps needs no change if the components own the pairing; confirm rather than assume.

## Acceptance criteria

- [ ] The decision above is settled and recorded here before any values are written.
- [ ] All five semantic pairings measure **≥ 4.5:1** in **both** themes, asserted by an automated sweep rather
      than by eye — the numbers above came from a script and the fix should be held to the same standard.
- [ ] The sweep **fails against the current palette first**, proving it measures the thing that is broken.
- [ ] The light theme is included. It is currently the worse of the two (2.86:1 measured) and was nearly
      overlooked because the dark sweep is the one that exists.
- [ ] `--b-color-danger-text` is folded into the new family rather than left as a special case, so the next
      person finds one pattern instead of one token plus a rule.
- [ ] Symbio is checked, or the risk is explicitly recorded as unassessed — a palette change is visible in
      every consumer and this repo cannot run it.

## Out of scope

- Any other contrast pairing in the palette (body text on surfaces, buttons, links). This is the semantic
  colour-on-its-own-tint family only; a wider audit is its own task if anyone wants one.
- The `--b-color-primary-alpha-*` gap — that is TASK-125, filed alongside this from the same sweep.

## Human test plan

- [ ] Read a `danger` and a `warning` badge on a real phone in **both** themes, in daylight. The measurement
      settles compliance; a human settles whether it now reads as *urgent* rather than merely legible — a
      contrast fix that makes danger look like a neutral chip has passed the number and failed the purpose.

## Implementation plan

_Populated when picked up._
