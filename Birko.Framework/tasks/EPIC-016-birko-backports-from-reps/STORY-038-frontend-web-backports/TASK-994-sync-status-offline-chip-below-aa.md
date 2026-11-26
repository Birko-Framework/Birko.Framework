---
id: TASK-994
parent: STORY-038
feature: null
status: done
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

The **syncing** chip has the same defect (found 2026-11-26 while preparing the options): `--b-color-info` on
`--b-color-info-light`, **3.29:1** light, **3.64:1** dark.

TASK-599's October fix moved `b-badge`, `b-stat` and the shell items to the `-text` tokens but missed this chip. Neither
gate caught it: the DesignTokens contrast gate never measures `--b-color-warning` on its tint (the chip's pair is not a
declared pair), and `device-fix-check` 3b renders only badges and toasts. The dead chip passes in light by only 0.05.

## Options (the fix colour is a visible change; show it first)

- **A. `--b-color-warning-text`**, as every other component that was moved in TASK-599 (light `#a65b05`; the contrast gate already measures this pair).
  Smallest change: AA, same look as the rest of the shared family.
- **B. The badge pair (`--b-badge-warning-*`, and `--b-badge-danger-*` for the dead chip)**, as Reps suggests: more margin
  and matches the urgent badges. But that moves sync status onto the badge's stronger colours, which is the question
  [[TASK-993]] holds open; decide it there or here, not twice.

## Decision 2026-11-26 (the user)

Shown side by side in both themes with measured contrast (artifact "Sync Chip Contrast"). The user asked whether the chips
could use the badge colours, and chose that, **option B**: each chip looks exactly like the badge of the same meaning.
Offline = the warning badge (`--b-badge-warning-*`), Failed = the danger badge (`--b-badge-danger-*`), Syncing = the info
badge (`--b-color-info-text` on `--b-color-info-light`, as `b-badge` paints info). A lost: just over AA (4.56–4.60), and the
failed chip would have kept its 0.05 margin. For sync status this settles part of [[TASK-993]]; the rest of TASK-993 (banners,
diffs, menus, stats) stays open.

## Acceptance criteria

- [x] The offline and syncing chips' text is AA in every theme, as rendered.
- [x] The dead chip measured in every theme, with its margin decided (A leaves it at 4.55:1 in light).
- [x] The gap closed: the chip's pairs measured by `device-fix-check` (rendered, every theme), so a component painting a
      base colour on its tint is caught; red before the fix.
- [x] Reps told the result for its TASK-123.

## Evidence 2026-11-26

Birko.Web `task/TASK-994` 8bcbd12 (starts v0.5.8), Playground f0c4a1c.
- `b-sync-status`: offline reads `--b-badge-warning-*`, syncing `--b-color-info-text` on `--b-color-info-light`, "not saved"
  `--b-badge-danger-*`, each falling back to the shared pair. No token changes.
- Worse than reported, found by the new check on main: in finstat the offline chip was **1.54:1** and syncing **1.84:1**;
  dark and inverse failed too (offline 4.22, syncing 3.64).
- `device-fix-check` 3b'': renders the chip online with pending and dead writes, then with the page offline, in light,
  dark, neon, finstat and inverse; each part must be AA and paint exactly what the `b-badge` of the same meaning paints.
  301/301 on the branch; **red** 20/301 on Birko.Web main. Components `tsc` clean. `verify` 0 failing on the branch (an
  earlier run of navigation timeouts was the machine: an endpoint scanner and the WSL VM at ~90% CPU, main timed out the
  same way).

## Out of scope

- The shared tokens' values and other components: [[TASK-993]].

## Human test plan

- [x] Take Reps (or the Playground) offline: read the offline chip on a phone in both themes; it reads as a warning and is
      legible.

**Merged 2026-11-26** on the user's word ("merge", after the Playground Sync status card got Offline / Pending / Not saved controls, Playground 27bdafb, and was offered for the phone check) into Birko.Web main 89673b6 (the merge commit, amended with [skip ci] at push), tagged **v0.5.8** and pushed 2026-11-26. On main: Components `tsc` clean, device-fix-check 301/301, verify 0 failing. Stays at verify until Reps is told on push and re-measures for its TASK-123.

**Done 2026-11-26.** Reps re-measured on v0.5.8 (89673b6), as rendered in desktop Chromium against its Testing host with a real offline state, a held in-flight send and a real refused write: offline 6.84 / 6.85, syncing 4.56 / 4.59, not saved 5.42 / 8.80 (light / dark), identical to ours. Reps copies tokens.css and themes/dark.css at build, so the badge tokens arrive with their dark values; its E2E 252/252 in both lanes, no Reps code change; its TASK-123 unblocked (Reps b9f2020), only its own phone check left.
