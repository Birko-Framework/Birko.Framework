---
id: TASK-726
parent: null
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `.sr-only` text in b-kanban, b-qty-stepper, b-rating and b-gallery does not scroll with its container

## Context

Found while fixing TASK-725's follow-up. The ClientApi.CSharp tester reported that `b-json-diff` stretched the
document: its `.sr-only` spans (`position: absolute`) had no positioned ancestor inside the component, so inside
BAppShell's scrolling `main` they extended the window instead. `b-json-diff` and `b-tabs` were fixed with
`:host { position: relative; }`, and Playground `sr-only-contain-smoke` guards them.

The same check, which scrolls a container and requires each `.sr-only` to move with it, **fails** for these four:
`b-kanban`, `b-qty-stepper`, `b-rating` and `b-gallery`. Their hidden text stays put when the container scrolls.
But a 900x600 probe measured **no document growth** for any of them, even with text written into their live regions.
`b-tabs` grew the document with its region empty. Why the two groups differ is not established.

## Acceptance criteria

- [ ] Explain why the four did not stretch the document where `b-json-diff` / `b-tabs` did, or find a setup where they do
- [ ] Give each a containing block for its `.sr-only`, without moving any other absolutely positioned part of it
      (drag ghost, popovers, zoom overlay), and add each to `sr-only-contain-smoke`

## Human test plan

- [ ] Each of the four inside a scrolling page container: no second scrollbar, and the screen-reader announcements
      still read (NVDA or Narrator)
