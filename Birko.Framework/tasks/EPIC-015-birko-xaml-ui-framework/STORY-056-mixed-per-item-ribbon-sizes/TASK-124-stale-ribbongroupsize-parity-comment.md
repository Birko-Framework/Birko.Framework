---
id: TASK-124
parent: STORY-056
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-07-31
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# The `RibbonGroupSize` doc comment describes a parity gap that no longer exists

## Context

Independent of the rest of the story — no dependency on TASK-119, and it can land immediately.

`b-ribbon.ts:35-37` still says:

> **Neither skin renders all four yet** — TASK-099/TASK-100 build them. As of TASK-098 `b-ribbon`
> renders every item as `medium` while the Avalonia `Ribbon` renders every item as `large` — a
> pre-existing parity gap named here so TASK-099 reconciles it deliberately.

**Both claims are stale**, measured 2026-07-31:

- TASK-099 and TASK-100 are **done** (STORY-049 closed 4/4), so all four variants render.
- The defaults now **agree**. `Birko.Xaml.Avalonia/Controls/Ribbon.cs:99` registers
  `PreferredGroupSizeProperty` with `RibbonGroupSize.Medium`, and its own doc at line 106 says
  *"Defaults to Medium: it matches what both skins rendered before the…"*. `b-ribbon.ts:636` and
  `:1093` both read `this.attr('preferred-group-size') || 'medium'`. Same default, both skins.

So the medium-vs-large gap was closed as part of STORY-049 and the comment was never updated.

**On Symbio's `preferred-group-size="large"`:** that is *not* a parity workaround, contrary to how it
reads from the outside. Both skins default to `medium`, so Symbio setting `large` is opting into a
roomier look. Worth stating explicitly in the corrected comment, because the next reader will otherwise
draw the same inference and "fix" a gap that is not there.

## Approach

Replace the stale paragraph with what is true now: all four variants render on both skins, and both
default to `medium`. Keep the "mirrors `RibbonGroupSize` in `RibbonModels.cs`; keep the two in step"
line — that is still correct and still load-bearing.

While in there, check the neighbouring comments for the same rot. The block at `b-ribbon.ts:280-283`
references TASK-098 tokens and is believed accurate, but a comment that names a task id is a comment
with an expiry date; verify rather than assume.

If [[TASK-120]] lands first it will rewrite this region anyway — in that case fold this in rather than
doing it twice. Independent, not order-dependent.

## Acceptance criteria

- [ ] The stale paragraph is replaced with the measured current behaviour
- [ ] The shared `medium` default is stated, with the note that Symbio's `large` is a preference and
      not a parity fix
- [ ] The "keep the two in step" line survives
- [ ] Neighbouring task-id-bearing comments in `b-ribbon.ts` are checked and corrected if also stale
- [ ] The equivalent comment on the C# side (`RibbonModels.cs`, `Ribbon.cs:106`) is checked for the
      mirror-image claim
- [ ] No behaviour change — comments only, and the suites confirm it

## Out of scope

- Changing either default. They agree at `medium`; whether `medium` is the *right* default is a
  separate question and not this task's to open.
- The mixed-size work.

## Human test plan

N/A — comment-only, no runtime surface. The claim was verified by reading both defaults; the
verification is recorded in Context above.
