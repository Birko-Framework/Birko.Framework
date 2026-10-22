---
id: TASK-729
parent: null
feature: null
status: verify  # verified with trusted key presses 2026-10-22; the human test plan is left
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-tag-input` loses focus after a tag is committed, so the next value typed goes nowhere

## Context

Reported by the ClientApi.CSharp session: in the FinStat web tester's compare page (and its batch runner), typing
`11111111`, Enter, `22222222`, Enter kept only the first tag. Right after the first Enter the deep active element
was `<body>`.

**Cause.** A commit (Enter, Tab, a separator, paste) and a Backspace removal call `update()`. The morph is positional,
so the new chip lands where the `<input>` was, and the focused element is replaced. `onUpdated` restored the buffer
but never the focus.

## Acceptance criteria

- [x] Type, Enter, type, Enter keeps both tags; a separator and a Tab commit keep focus in the input
- [x] Repeated Backspace removes one tag per press
- [x] A blur-triggered commit, `setTags()` and `clear()` do not pull focus back into the control
- [x] A regression check with real key presses

## Human test plan

- [ ] In the FinStat web tester's compare page and batch runner, enter several codes with Enter, then remove two with
      Backspace

## Outcome (2026-10-22)

- `_updateKeepingFocus()` re-renders and refocuses the input when focus was anywhere inside the control. Commits, paste
  and removals use it.
  - The blur path does not refocus, because focus has already left by the time it runs.
  - `setTags` / `clear` keep plain `update()`.
- **Gate.** Playground `keyboard-check.mjs` 32/32 with 5 new checks, using trusted presses through puppeteer.
  - Without the refocus all 5 fail: focus on `body`, one tag kept.
  - `verify.mjs` stays green, including form-assoc 104/104, bare 113/113 and backport 324/324.
- Committed to Birko.Web and the Playground, not pushed.
