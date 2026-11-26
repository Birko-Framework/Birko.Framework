---
id: TASK-599
parent: STORY-038
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
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

- [x] The decision above is settled and recorded here before any values are written.
- [x] All five semantic pairings measure **≥ 4.5:1** in **both** themes, asserted by an automated sweep rather
      than by eye — the numbers above came from a script and the fix should be held to the same standard.
- [x] The sweep **fails against the current palette first**, proving it measures the thing that is broken.
- [x] The light theme is included. It is currently the worse of the two (2.86:1 measured) and was nearly
      overlooked because the dark sweep is the one that exists.
- [x] `--b-color-danger-text` is folded into the new family rather than left as a special case, so the next
      person finds one pattern instead of one token plus a rule.
- [x] Symbio is checked, or the risk is explicitly recorded as unassessed — a palette change is visible in
      every consumer and this repo cannot run it.

## Out of scope

- Any other contrast pairing in the palette (body text on surfaces, buttons, links). This is the semantic
  colour-on-its-own-tint family only; a wider audit is its own task if anyone wants one.
- The `--b-color-primary-alpha-*` gap — that is TASK-125, filed alongside this from the same sweep.

## Human test plan

- [x] Read a `danger` and a `warning` badge on a real phone in **both** themes, in daylight. The measurement
      settles compliance; a human settles whether it now reads as *urgent* rather than merely legible — a
      contrast fix that makes danger look like a neutral chip has passed the number and failed the purpose.
  - **Not run 2026-11-24** (needs a real phone in daylight). On a desktop screen the same day, by the agent at the user's request, in Chrome with real clicks and keys, Playground built from Birko.Web main v0.5.3: danger and warning
    badges measure 4.55 and 4.58 in light, 5.40 and 4.60 in dark, and side by side with a neutral Draft chip danger
    still reads red and urgent in both themes.
  - **Run 2026-11-26 by the user on a phone** (Playground over Tailscale, Birko.Web v0.5.7): every badge readable, but
    "warning is subtle and danger would probably be more red". Passed the number, failed the purpose, so the colours
    changed (below). The step stays open for a re-read of the new colours on the phone.
- [x] Re-read the new danger and warning badges (option B) on the phone, both themes, in daylight: they read as urgent.

## Decision 2026-11-26 (the user)

Four options were shown side by side in both themes with measured contrast (artifact "Badge Urgency Options"): A today's
pale tint, **B a stronger tint with a deeper text**, C a solid fill for danger and warning, D B plus a border and a shape.
The user picked **B, for the badge only for now**; whether the shared tokens (diffs, banners, menus, stats) should move the
same way is deferred to [[TASK-993]]. C lost as too loud for a busy table and, as a token change, would have turned every
removed diff line solid red; D's border and shape are left for TASK-993 to weigh.

## Evidence 2026-11-26

Birko.Web `task/TASK-599` d536f8a (joins v0.5.7), Playground 26d987b, Framework (this commit).
- New tokens in Birko.DesignTokens `tokens.json`: `--b-badge-{danger,warning}-{bg,text}`. Light `#fecaca`/`#a11a1a` (5.42:1) and
  `#fde68a`/`#7c3a03` (6.84:1); dark `#5f1a1a`/`#fecaca` (8.80:1) and `#5c3d06`/`#fcd34d` (6.85:1); neon, finstat and inverse
  point them at their own `--b-color-*-light` / `-text`, so they look as before. `b-badge` reads them, falling back to the
  shared pair. The shared tokens and every other component are unchanged. Generated CSS + AXAML; `verify` clean; the
  contrast gate gained the two badge pairs (296 pairs, none under 4.5:1); DesignTokens tests 59/59; Avalonia 448/448
  (the Avalonia badge has no danger / warning variant yet, so nothing there paints the new pair).
- `device-fix-check` 261/261: danger and warning badges paint exactly the new pair in light and dark, keep each other
  theme's own pair, and the shared tokens keep their values. **Red** 4/261 on Birko.Web main. `verify` 0 failing,
  Components `tsc` clean.
- **Push order:** the Framework's token tests compare `tokens.json` with Birko.Web's CSS, so the Framework commit must not
  reach CI before this branch is merged into Birko.Web main.

## Implementation plan

_Populated when picked up._

## Progress

**2026-10-14 — done in Birko.Web v0.1.10, with [[TASK-130]]'s gate.** In `verify` for the human test plan (a danger
and a warning badge read on a phone, in daylight, both themes).

- **Decision (owner, 2026-10-14): darken the text, keep the tints.** `--b-color-<x>-text` for all five colours in
  every theme; `--b-color-danger-text` was already that token and is now one of the family (light gets an explicit
  value instead of `var(--b-color-danger)`). Each value is the smallest step toward black (light surfaces) or white
  (dark) that clears 4.5:1, so the hue stays: light warning `#d97706` → `#a65b05` (2.86 → 4.5+), dark danger keeps
  `#f87171`. finstat (owner: fix with the rest): primary/success `#18794f`, warning `#8b6712`, info `#33708c`.
- **Wider than the five pairs**, by the same decision: text on solid fills (`--b-text-on-<x>`, dark text on
  success/warning/info and on every finstat fill), muted/secondary text in neon, finstat and inverse, and `inverse`'s
  tints (dark.css's; white text on the page's pastels was 1.10:1).
- **Components:** `b-badge`, `b-stat`, the ribbon / sidebar / shell active items and the tree-menu danger hover read
  `--b-color-<x>-text`; `b-toast`, `b-button` danger and the danger counters read `--b-text-on-<x>`. Every reference
  falls back to the old token.
- **Sweep:** `device-fix-check.mjs` § 3b measures the rendered badges and toasts in all eight compositions at 4.5:1;
  it replaced the dark-only "≥ 3:1 stays readable" check. **Failed on the old palette** (light warning badge 2.86,
  toasts 3.19–3.68), passes now.
- **Symbio: not run, risk recorded.** Symbio forks its own `tokens.css`. With the fallbacks it renders exactly as
  before until it adds the new tokens, so nothing changes for it on upgrade; adopting the fix means copying the new
  tokens into its fork. The visual change (darker badge text, dark text on toasts) is unassessed in Symbio.
- Reps needs no change for the badges, which the components own; its TASK-123 can re-measure against v0.1.10.

**Merged 2026-11-26** on the user's word ("merge", given after the branch build was offered for the phone re-read with "if they read as urgent, say merge") into Birko.Web main c2943f1; local tag v0.5.7 moved onto it (not pushed). On main: Components `tsc` clean, DesignTokens `verify` clean against the real Web checkout, device-fix-check 261/261, verify 0 failing. Done; the wider question is [[TASK-993]].

**Consumers, 2026-11-26.** Symbio (built from v0.5.7): no badge overrides; the Blocked (danger) badge on the customers list
reads well in light and dark (screenshots). The four new tokens tripped Symbio's own token-parity guard (its TASK-1131,
"4 Birko base tokens not in Symbio css/tokens.css"); Symbio now carries the light and dark values in its token copy (a fork
of ours: without the dark values, dark would inherit the light tint). A consumer that keeps such a copy must add new base
tokens per theme. Reps renders no `b-badge`: its remaining defect is `b-sync-status`, filed as [[TASK-994]].
