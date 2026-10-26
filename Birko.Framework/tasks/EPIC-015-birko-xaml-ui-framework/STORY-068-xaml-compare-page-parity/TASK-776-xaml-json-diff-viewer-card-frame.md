---
id: TASK-776
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
priority: P2
assignee: ai
created: 2026-10-26
depends-on: [TASK-772, TASK-773]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `JsonDiffViewer` in the web's data-viewer card frame

## Context

FEATURE-020 D6, changed after sign-off. Requested by the FinStat desktop tester's session (clientapi-csharp-d1) for its
user: the viewer had no frame, so in the tester's Compare window its blocks did not read as distinct. On the web,
`b-json-diff` renders inside the shared `.data-viewer-card` (`Birko.Web.Components/src/shared-styles.css`): a card on
`--b-bg-tertiary` with a border, radius and clipped overflow; a `.data-viewer-header` bar on `--b-bg` with xs / md padding
and a bottom border; a `.body` padded `--b-space-md`. The side-by-side panes sit in that body a `--b-space-md` gap apart,
with no frame of their own — the user chose web parity there over a divider or per-pane frames (2026-10-26).

## Acceptance criteria

- [x] The viewer, header included, sits in a card: `BBgTertiaryBrush` background, 1 px `BBorderBrush` border, `BRadius`
      corners, content clipped to them.
- [x] The header is a bar: `BBgBrush` background, 12 / 4 padding, a 1 px divider under it.
- [x] The tree body and the side-by-side grid are padded 12; the panes keep the 12 gap and get no frame.
- [x] On by default, as on the web; `IsFramed = false` leaves the bare header and body for a host that frames it itself.
- [x] Headless tests for each, proved able to fail; Light and Dark screenshots.
- [x] README, CLAUDE.md and Recent Updates updated.

## Human test plan

- [ ] 1. Gallery, diff viewer page: the viewer is a bordered, rounded card; the header bar has its own background and a
   line under it; the tree is inset from the card's edge.
- [ ] 2. Switch to Side by side: both panes are inside the same card, a gap apart, no frame around each.
- [ ] 3. Repeat under Dark, Neon and Finstat: the card's border and the header bar stay visible.
- [x] 4. The FinStat desktop tester's Compare window, rebuilt on this commit: the diff reads as its own framed block.

## Review log

- 2026-10-26 — Built; **verify**. `Controls/JsonDiffViewer.cs`: `IsFramed` (default `true`, re-renders), `Card` and
  `CardHeader` wrap the existing header and body (classes `card` / `card-header`); the body and split grid take a 12
  margin. Tokens bound with `GetResourceObservable`, so a theme switch follows. The header's title, summary and buttons
  are unchanged.
- Tests: `JsonDiffViewerFrameTests` 6 + 2 screenshots. **Proved they can fail:** `IsFramed` defaulting to `false` and
  the header without its divider failed 5 of 6. Avalonia suite 403/403.
- Looked at (headless, Light and Dark): both views read as the web's card.
- 2026-10-26 — Human test plan step 4 passed: the user checked the framed viewer in the desktop tester's Compare window,
  rebuilt on `327bd9d4` ("looks good", relayed by the tester session). Steps 1–3 (Gallery, every theme) not run yet;
  stays at verify.
