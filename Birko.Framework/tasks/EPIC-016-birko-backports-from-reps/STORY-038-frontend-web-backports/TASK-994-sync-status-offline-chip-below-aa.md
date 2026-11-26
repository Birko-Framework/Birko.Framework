---
id: TASK-994
parent: STORY-038
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-26
depends-on: []
blocks: []
related: [TASK-599, TASK-993, TASK-130]
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-sync-status's offline chip is below AA (2.86:1 in light)

## Context

Reported by the WorkoutTracker (Reps) session on 2026-11-26, re-measuring its TASK-123 on Birko.Web v0.5.7 (Reps
aaca830). Reps shows no `b-badge`, so TASK-599 changed nothing it shows. The defect its TASK-123 originally measured
is in `b-sync-status`: the **offline** chip paints `--b-color-warning` text on `--b-color-warning-light`
(`Birko.Web.Components/src/feedback/b-sync-status.ts:81-82`, confirmed in the source). Measured as rendered (desktop Chromium,
composited backgrounds):

| Pairing | light | dark |
|---|---|---|
| offline chip, `--b-color-warning` on `--b-color-warning-light` | **2.86:1** | **4.22:1** |
| dead chip, `--b-color-danger-text` on `--b-color-danger-light` | 4.55:1 | 5.40:1 |

TASK-599's October fix moved `b-badge`, `b-stat` and the shell items to the `-text` tokens but missed this chip. Neither
gate caught it: the DesignTokens contrast gate never measures `--b-color-warning` on its tint (the chip's pair is not a
declared pair), and `device-fix-check` 3b renders only badges and toasts. The dead chip passes in light by only 0.05.

## Options (the fix colour is a visible change; show it first)

- **A. `--b-color-warning-text`**, as every other component that was moved in TASK-599 (light `#a65b05`; the contrast gate already measures this pair).
  Smallest change: AA, same look as the rest of the shared family.
- **B. The badge pair (`--b-badge-warning-*`, and `--b-badge-danger-*` for the dead chip)**, as Reps suggests: more margin
  and matches the urgent badges. But that moves sync status onto the badge's stronger colours, which is the question
  [[TASK-993]] holds open; decide it there or here, not twice.

## Acceptance criteria

- [ ] The offline chip's text is AA in every theme, as rendered.
- [ ] The dead chip measured in every theme, with its margin decided (A leaves it at 4.55:1 in light).
- [ ] The gap closed: the chip's pairs measured by `device-fix-check` (rendered, every theme), so a component painting a
      base colour on its tint is caught; red before the fix.
- [ ] Reps told the result for its TASK-123.

## Out of scope

- The shared tokens' values and other components: [[TASK-993]].

## Human test plan

- [ ] Take Reps (or the Playground) offline: read the offline chip on a phone in both themes; it reads as a warning and is
      legible.
