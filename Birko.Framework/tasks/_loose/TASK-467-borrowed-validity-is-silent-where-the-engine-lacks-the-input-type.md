---
id: TASK-467
parent: null
feature: FEATURE-001
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-09-19
depends-on: []
blocks: []
related: [TASK-035, TASK-466]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Borrowed validity enforces nothing, silently, where the engine lacks the input type

Found by running [[TASK-035]]'s *"verify in Chromium + Firefox + WebKit"* item, fourteen weeks after it
was written — `form-assoc-smoke` is **103/104 in WebKit**, failing only
`b-date-picker native mirrors min via rangeUnderflow`.

## What is actually happening

TASK-035's design is *"validity is **borrowed**, not reimplemented"*: `syncFormState()` mirrors the
inner native control's own `validity` verbatim, so `min` / `max` / `step` / `pattern` / `type=url` are
enforced by the browser rather than re-derived and left to drift. That is the right call and this does
not change it.

But it assumes the engine **has** the primitive. Measured directly, same markup, both engines:

| | reported `type` | `rangeUnderflow` | `validity.valid` |
|---|---|---|---|
| Chromium | `date` | **true** | false |
| WebKit (Playwright build) | **`text`** | false | **true** |

`<input type="date" min="2026-01-01" value="2025-06-01">` falls back to a text input in that WebKit
build, so there is no range validity to borrow — and the control reports **valid**. A form that relies
on `min` gets no enforcement and **no signal that it got none**.

⚠ **Scope this honestly before acting on it.** This is Playwright's WebKit, not Safari: real
Safari on macOS/iOS does implement `<input type="date">`. What is demonstrated is the *class* of
problem — any engine without the primitive silently enforces nothing — not that Safari is affected.
**Measure on a real Safari before writing anything user-facing about it.**

## Why it is worth a task rather than a footnote

The framework already reasons about controls with no usable native primitive: `validationSource()`
returns `undefined` for searchable `b-select`, `b-multi-select`, `b-tag-input` and the pickers, and
those get a generic `required`-only check. That decision is made **per control, at author time**. The
case here is the same condition arising **per engine, at runtime**, where nothing looks for it.

So a consumer writing `<b-date-picker native min="…">` gets enforcement on some of their users'
browsers and not others, with the same markup and no warning.

## Options

1. **Detect and report.** A control in `native` mode can compare the inner element's reported `type`
   against the one it asked for; a mismatch means the engine declined it. That is a one-line check at
   sync time. What to *do* with it is the actual decision — a console warning is noise on every render,
   an exception is far too much for a progressive-enhancement fallback.
2. **Fall back to the non-native path** when the primitive is missing, so the component's own picker
   and its own validation take over. Most correct, most work, and it changes rendering on the affected
   engines.
3. **Document the limit** and leave the behaviour. Cheapest; acceptable only if option 1's detection is
   genuinely not worth the line.

## Acceptance

1. Real Safari is measured first. The whole shape of this depends on whether it is affected or whether
   this is only the Playwright build.
2. Whichever option: `webkit-check.mjs` stops allow-listing this check by name. It is excused there
   **because this task exists**, and an allow-list entry that outlives its task is the stale blanket
   this family keeps recording.
3. If detection lands, it is pinned in a way that can fail — a check that only ever runs on an engine
   nobody tests is not a check.

## Out of scope

- The borrowed-validity design itself, which is right and is what makes `min`/`step`/`pattern` work
  at all.
- [[TASK-466]] (Firefox shows no validation bubble for a shadow-DOM control) — a different engine and
  a different mechanism, though both were found by the same "run it in another engine" pass.
