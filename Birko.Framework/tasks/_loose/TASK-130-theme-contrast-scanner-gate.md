---
id: TASK-130
parent: null
feature: null
status: todo  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
priority: P1
assignee: ai
created: 2026-08-01
depends-on: []
blocks: []
related: [TASK-103]
pr: null
github-issue: null
jira-key: null
---

# Scan every shipped theme for colour contrast, and gate it like the drift check

**Framework-wide, spanning `Birko.DesignTokens` (the generator + `verify`), `Birko.Web.Components`
(`css/themes/*.css`) and `Birko.Xaml.Avalonia` (the six generated AXAML dictionaries).** Filed in `_loose`
alongside [[TASK-106]] because no epic covers the token/theme layer: EPIC-001 is `affects:
[Birko.Web.Components]` and EPIC-015 is the XAML skin — this is the thing underneath both.

## Context

A theme is a set of token *overrides*, and nothing checks that the overrides still compose into legible
pairs. Every defect below shipped, and each was found by a person looking at a screen rather than by a gate.

**What triggered this (2026-08-01).** The device pass found `b-stale-banner`'s offline warning unreadable
in dark. Cause: the semantic `--b-color-*-light` tokens are TINT BACKGROUNDS, `dark.css` darkened only
`--b-color-primary-light`, and the other four kept their light-page pastels while `--b-text` flipped to
near-white — **1:1 to 1.12:1**, i.e. invisible rather than merely low-contrast. Fixed in `dark.css`
(e07f9d3) and mirrored into Symbio, which forks its own `tokens.css`.

**Then a one-off scan of all five themes found the same species everywhere.** Two foreground pairs ride on
the tint tokens and both matter — `--b-text` on the tint (`b-stale-banner`, `b-sync-status`,
`b-markdown-editor`'s `<mark>`) and `--b-color-{name}` on the tint (`b-badge`, `b-stat`):

| theme | failing pairs | measured |
|---|---|---|
| **base / light** | `warning` badge | **2.86:1** — in the DEFAULT theme, on `#d97706` over `#fef3c7` |
| **inverse** | all 5 text pairs, + `warning` badge | **1.10 / 1.11 / 1.22 / 1.12 / 1.22:1** |
| **finstat** | all 5 badge pairs | **2.13 / 1.54 / 2.36 / 1.84 / 2.13:1** |
| dark, neon | none | clean |

**`inverse` is the interesting one, and it is why this cannot be a per-file lint.** It is a deliberately
PARTIAL theme: it flips surfaces/text to dark charcoal and *intentionally inherits* the brand colours from
whatever page theme is active, so the pale tints stay while `--b-text` becomes `#ffffff`. Reading
`inverse.css` alone shows nothing wrong — the failure only exists in the *composition* of two themes. A
scanner therefore has to resolve tokens the way the cascade does (the one-off used a real browser and
`getComputedStyle`), not parse declarations file by file.

**`finstat` shows the gate needs recorded exceptions, not just a threshold.** Its accents are the `@clr`
brand colours taken as-is (`#25ba7a`, `#fab921`, `#f26d4f`, `#54b9e8`); every one is a mid-tone sitting
close to its own pale tint. That cannot be resolved by copying a Birko value — it means darkening the brand
(off-brand) or re-deriving the tints, which is a palette decision for the brand owner. A gate with no way to
record "known, measured, accepted" either blocks forever or gets switched off.

**Where it belongs.** `Birko.DesignTokens` owns `tokens.json` (the single source) and the
`extract`/`generate`/`verify` CLI, and `verify` already diffs regenerated CSS **and** AXAML against disk.
Contrast is the same shape of check on the same inputs. Note the lesson already recorded in that project's
CLAUDE.md: the AXAML drift went unnoticed because **nothing runs a CLI verb by itself** — the fix was
`AxamlParityTests` gating it from the suite. A contrast verb needs the same suite-level gate or it will sit
unrun for months.

**Pairs worth covering** — the tint families are only the ones that bit us; the same scan is nearly free
for the rest:
- `--b-text` / `--b-text-secondary` / `--b-text-muted` on `--b-bg` / `-secondary` / `-tertiary` / `-elevated`
- `--b-text` and `--b-color-{name}` on `--b-color-{name}-light` (the two pairs above)
- `--b-text-inverse` on solid `--b-color-{name}` fills — `b-toast` writes white on the success/warning
  fill, which in dark measures **3.30:1 / 3.19:1**, under AA. Known and deliberately left standing today
  (see Out of scope), and a good check that the gate reports what is already accepted.
- `--b-tooltip-text` on `--b-tooltip-bg`

## Acceptance criteria

- [ ] A contrast scan exists over the shipped themes, resolving tokens through the real cascade so a
      PARTIAL theme (`inverse`) is checked **as composed over each page theme**, not as a standalone file
- [ ] It covers the pair families listed above, for every theme in `Birko.Web.Components/css/themes/` plus
      the base `:root`
- [ ] Thresholds are per pair kind and justified in code (AA 4.5:1 body text; 3:1 for large/bold badge
      text), not one magic constant
- [ ] Each reported pair carries its **measured ratio and both hex values** in the message — a bare
      pass/fail is not actionable and hides a near-miss
- [ ] ALL failing pairs are reported in one run (a per-pair assert that throws on the first hid an
      identical `warning` failure behind `success` while this was being written)
- [ ] Known-accepted pairs are recorded **with their numbers and a reason** and are re-measured (so a
      recorded exception that silently gets *worse* still fails), not suppressed by name
- [ ] The scan runs from `Birko.DesignTokens.Tests`, not only as a CLI verb
- [ ] The 12 failing pairs above are each either fixed or recorded as a measured exception — the
      `base`/`warning` badge one first, since it is the default theme
- [ ] The generated AXAML dictionaries are covered too, or a line in the task records why the CSS scan is
      sufficient for them (they emit from the same `tokens.json`)
- [ ] `Birko.DesignTokens/CLAUDE.md` documents the new verb + gate next to the drift-gate section

## Out of scope

- **Fixing the finstat palette.** All five of its badge pairs need a brand decision (darken the accents, or
  re-derive the tints); this task records them as measured exceptions and stops there.
- **The `b-toast` / `--b-text-inverse` pair.** Fixing it properly means a text-only token — the pattern
  `dark.css` already established with `--b-color-danger-text`, which exists so a fill colour need not move.
  That is a catalogue API change affecting `b-toast`, `b-badge` and `b-stat`; it should be its own task once
  this scan has established the full list of fill pairs that need one.
- **Consumer forks.** Symbio ships its own `tokens.css` copy (its own dark block, its own accents) and now
  has its own runtime guard, `tests/ui-e2e/dark-tint-contrast-check.spec.ts`. Making this scanner runnable
  against an arbitrary consumer's token file is a follow-up; the framework's own themes come first.
- Non-colour accessibility (focus visuals are [[TASK-103]], hit targets, motion).

## Human test plan

- [ ] Pick one pair the scan reports as failing and one it passes, open the Birko.Web Playground in that
      theme, and confirm by eye that the verdicts match what you see — a contrast number that disagrees with
      the screen means the wrong pair is being measured (this is exactly how the `b-stale-banner` defect was
      first noticed, and how the "measuring the light theme" harness bug was caught).
- [ ] Verify the gate FAILS when it should: revert one of the four `dark.css` tint tokens to its pastel and
      confirm the scan goes red naming that token. A gate never seen failing is not known to work.
- [ ] Check a recorded exception still reports its measured number in the output, so a silently worsening
      exception is visible rather than merely tolerated.

## Implementation plan

_Populated by `/tasks plan TASK-130` — leave empty until then._
