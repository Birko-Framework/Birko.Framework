---
id: TASK-736
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-22
depends-on: [TASK-731, TASK-732]
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: key tips (Alt shows letters for keyboard access)

## Context

FEATURE-019 D7 (approved by the user 2026-10-22): Pressing Alt shows a small letter on every tab; pressing it opens that tab and shows letters on its commands, so anything in the ribbon can be reached in two or three key presses.

No key tips exist. Alt is also the browser menu key on some platforms, so the trigger needs care.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [ ] Alt+Windows key (Ctrl+Option on a Mac) shows tab tips, as in Microsoft 365 in the browser (decided by the user,
      Q2); an attribute changes the combination; a letter opens the tab and shows command tips; Escape goes back one
      level
- [ ] Tips are declared or derived (first free letter) without collisions, per locale
- [ ] Menus and galleries get their own level
- [ ] Does not break typing in inputs, nor the browser's own Alt behaviour
- [ ] keyboard-check.mjs covers it with real key presses
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Reach three different commands with key tips only; type Alt inside an input and check nothing fires
