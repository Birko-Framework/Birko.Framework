---
id: TASK-485
parent: STORY-057
feature: FEATURE-001
status: todo
priority: P3
assignee: ai
created: 2026-09-24
depends-on: [TASK-484]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Extend adornment slots to the other text-like controls; move `b-search-input` onto them

## Context

Once [[TASK-484]] gives `b-input` its `prefix` / `suffix` slots and `clearable`, the same shape likely
helps the other controls that render a text-like field in their own shadow root. Candidates, from
`Birko.Web.Components/src/inputs/`:

- `b-textarea`: suffix only makes sense top-aligned; may not qualify
- `b-date-picker` (custom mode `.dp-input`), `b-time` (`.tp-input`), `b-datetime-picker`: a calendar/clock
  icon as prefix, `clearable` for optional dates
- `b-select` combo mode (`.combo-input`): already has `clearable`, so reconcile it rather than duplicate it
- `b-tag-input`: prefix icon; `clearable` would mean "remove all tags", a different semantic
- `b-search-input`: today it **hand-rolls** its icon and × with a per-size padding workaround. Rebuilding it on
  `b-input`'s slots deletes that copy.

Not every candidate should get the slots. This task is a per-control decision first, then the rollout.

## Acceptance criteria

- [ ] Each candidate above has a recorded verdict (adopt / not needed / different semantic) with its reason, in this task or STORY-057
- [ ] Adopted controls get `prefix` / `suffix` + `clearable` with the same names, events, a11y and padding behaviour as TASK-484. The shared logic lives in one place (a shared sheet/helper), not copied per control
- [ ] `b-search-input` renders its icon and × through the shared mechanism; its per-size padding workaround is gone; `label-clear` still works
- [ ] `b-select`'s existing `clearable` either sits on the shared mechanism or its difference is documented
- [ ] `API.md` updated for every control that changed

## Out of scope

- `b-input` itself — [[TASK-484]]
- New adornment kinds beyond prefix/suffix/clearable (e.g. a password reveal toggle) — file separately if wanted

## Human test plan

_To be written at `/tasks plan TASK-485`, per adopted control: sizes, keyboard, SR label, both themes. Same shape as TASK-484._

## Implementation plan

_Populated by `/tasks plan TASK-485` — leave empty until then._
