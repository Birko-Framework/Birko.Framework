---
id: TASK-774
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
priority: P2
assignee: ai
created: 2026-10-26
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `TagInput` control (Birko.Xaml.Avalonia) — a port of `b-tag-input`

## Context

FEATURE-020 D8 + D12 (strict parity; D9's validator hook was removed). Port
`Birko.Web.Components/src/inputs/b-tag-input.ts` (374 lines): label, placeholder, `separators` (characters that split
typed and pasted text into tags), duplicate handling, `max-count`, the `reject` event with its reason, the `change`
event, and backspace-removes-last. The tester uses it for the list of IČOs.

`Form` already has a private inline tags editor for `FieldType.Tags` (`Form.cs` `BuildTags`, from TASK-057: Enter adds,
Backspace removes, no separators, no duplicate or max-count rule). Switch it to the new control so there is one tags
editor; keep its behaviour for existing forms or record any visible difference.

## Acceptance criteria

- [x] Properties mirror the web attributes: `Placeholder`, `Separators`, `MaxCount`, duplicate rule as on the web,
      `Tags` (bindable list), `IsReadOnly`; events `TagsChanged` and
      `TagRejected` (reason: duplicate / max-count — the web set is these two; the first draft said "empty" too, which the web does not raise).
      **Not built:** `Label` and size variants — no other Birko XAML input carries its own label (the Form draws it) or
      size variants; see the Review log.
- [x] Pasting `a, b;c` with separators `,;` adds three tags.
- [x] `Form` `FieldType.Tags` uses the control.
- [x] Headless tests: separators on typing and paste, duplicates, max count, reject reasons, backspace, bound list sync,
      the Form field.
- [x] Gallery page; docs and Recent Updates updated.

## Out of scope

- Per-tag validation (FEATURE-020 D9, removed).

## Human test plan

1. Gallery tag input page: type `12345678,` — a chip appears; type the same value again — it is rejected and the
   page shows the reason.
2. Paste a list of IDs separated by commas, semicolons and spaces (per the configured separators): each becomes a chip.
3. With a max count of 3, try a fourth: rejected with the max-count reason.
4. Backspace in the empty box removes the last chip; the × on a chip removes that chip.
5. Open a form with a Tags field: it behaves the same way.
6. Check under each theme.

## Implementation plan

## Review log

- 2026-10-26 — Built; **verify** (Human test plan needs a person at the Gallery). `Birko.Xaml.Avalonia/Controls/TagInput.cs`
  (+ `TagRejectReason`, `TagRejectedEventArgs`, `TagEventArgs`): `Tags` two-way `IReadOnlyList<string>`, `Placeholder`,
  `Separators` (default comma / newline / tab), `MaxCount`, `AllowDuplicates`, `IsReadOnly`, `HasError`, `Value`;
  `TagAdded` / `TagRemoved` / `TagRejected` / `TagsChanged`; `SetTags`, `Clear`, `PasteText`. The web's label / hint /
  error shell is not part of it: on the desktop the `Form` (or the host) draws the field's label. Size variants not
  ported (no other Birko XAML input has them).
- `Form` `FieldType.Tags` now renders `TagInput`, refilling the model's list in place. **Visible change** for existing
  forms: a duplicate tag is refused and a comma commits a tag (before, only Enter added and duplicates were kept). The
  three existing Form tag tests pass unchanged.
- Bug found by the tests, fixed before close: re-rendering cleared the panel, which took focus from the text box; its
  `LostFocus` handler re-entered the render and added the box twice (InvalidOperationException). Only the chips before
  the box are replaced now, and a test asserts focus stays in the box while chips are added.
- Tests: `TagInputTests` 16 + 2 screenshots. **Proved they can fail:** dropping the duplicate rule failed 3; not trimming
  a typed tag failed 1 (after a spaced case was added — it was missed at first). Avalonia suite 374/374.
- Looked at (headless, Light and Dark): chips, the danger border, read-only. **The placeholder does not show:** the
  Birko `TextBox` template has no watermark presenter, the same gap as TASK-768's `PasswordChar`. Recorded there, not
  fixed here (it affects every Birko text box). The Human test plan's placeholder step stays, and will fail until then.
- Gallery: Forms & data tab, "Tag input": up to 5 IČOs, separators `,; ` + newline / tab, last event shown below.
