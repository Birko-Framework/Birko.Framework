---
id: TASK-466
parent: null
feature: FEATURE-001
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-19
depends-on: []
blocks: []
related: [TASK-001, TASK-035, TASK-136]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Firefox shows no validation bubble for an invalid `b-*` control, and logs an error instead

Found by running [[TASK-001]]'s sixteen-week-old *"Verify in Chromium + Firefox"* item, which had sat
unrunnable because `verify.mjs` launches Chromium only.

## What happens

A `<form>` containing an invalid form-associated `b-*` control, with a real `type="submit"` button:

| | submit suppressed | native validation bubble | console |
|---|---|---|---|
| Chromium | yes | **shown** | clean |
| Firefox | yes | **never shown** | `Error: The invalid form control with name='x' is not focusable.` |

The **correctness half agrees** — both engines refuse to submit, which is what
`form-assoc-smoke` § 8i asserts and it passes in both (113/113 for `bare-smoke` in both engines too).
What differs is that Firefox cannot focus the control in order to show its bubble, because the
focusable element lives inside a **shadow root**, so it gives up and logs.

⚠ **Measured, after getting it wrong first.** The obvious reading is that the harness positions its
test forms at `left:-9999px` and an off-screen control is not focusable. That is not it: a probe that
built the same form **on-screen** produced the error identically — 2 errors for 2 forms, one
off-screen and one visible, both with `submitted: false`. The cause is the shadow boundary, not the
position.

## Why it matters

The user sees the form **do nothing**. No bubble, no focus jump, no message — just a Save button that
does not save. In Chromium the same page explains itself.

`form-assoc-smoke` § 8i already names this population precisely: *"A page that wrapped `b-*` in a
`<form>` with a real `type="submit"` button and did its own validation in the submit handler used to
get that handler called unconditionally… Now the browser refuses first and the handler never runs."*
On Firefox such a page additionally has no native fallback to fall back to.

**Consumers using `b-form` are unaffected** — `validate()` renders its own errors (TASK-136), which is
most of this family's surface. The exposed population is a raw `<form>` relying on native constraint
validation.

## Options, none of them free

1. **`setValidity`'s third argument.** `ElementInternals.setValidity(flags, message, anchor)` takes an
   *anchor* element — the thing the browser focuses and points the bubble at. If the components pass
   their inner control as the anchor, Firefox may be able to resolve it. **Measure whether Firefox
   honours an anchor inside a shadow root before building anything on it**; if it does, this is the
   fix and it is small.
2. **Render the message ourselves** when the browser will not, which is what `b-form` already does —
   but doing it in the control means deciding where it goes in `bare` mode, which has no error row by
   construction ([[TASK-001]]).
3. **Document it and leave it**, pointing consumers at `b-form` or `novalidate` + their own messages.
   Cheapest, and the least honest of the three if option 1 turns out to work.

## Acceptance

1. Option 1 is **measured first**, in Firefox, before any option is chosen — the whole task turns on
   whether an anchor crosses the shadow boundary there.
2. Whatever is chosen, `cross-engine-check.mjs` stops allow-listing the message by name. It is
   currently excused there *because this task exists*; an allow-list entry that outlives its task is
   the thing this family keeps recording as a stale blanket.
3. The behaviour is pinned per engine. Both engines suppressing the submit is the half that already
   works and must keep working — a fix that restores the bubble by making the form submittable would
   be a regression, not a fix.

## Out of scope

- Chromium's bubble content or styling.
- `b-form`'s own error rendering, which is unaffected and is the recommended path.
