---
id: TASK-774
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Properties mirror the web attributes: `Label`, `Placeholder`, `Separators`, `MaxCount`, duplicate rule as on the
      web, `Tags` (bindable list), `IsReadOnly`, size variants as the other Birko inputs; events `TagsChanged` and
      `TagRejected` (reason: duplicate / max-count / empty, exactly the web set).
- [ ] Pasting `a, b;c` with separators `,;` adds three tags.
- [ ] `Form` `FieldType.Tags` uses the control.
- [ ] Headless tests: separators on typing and paste, duplicates, max count, reject reasons, backspace, bound list sync,
      the Form field.
- [ ] Gallery page; docs and Recent Updates updated.

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
