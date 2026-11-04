---
id: TASK-814
parent: EPIC-001
feature: null
status: done  # signed off by the user 2026-11-04
priority: P3
assignee: ai
created: 2026-11-04
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-multi-select's search box keeps its text after closing, over an unfiltered list

Found by the user in Symbio (TASK-1030, the platform settings page's modules picker, 2026-11-04): type in a
`searchable` b-multi-select's search box, close the list, open it again — the box still shows the old query, but the
list holds every option, unfiltered.

## Context

`_closeDropdown` resets `_filter` and refreshes the options, but leaves `.dd-search`'s value; `_refreshOptions` keeps
the search wrap, so the old text survives. b-select's combobox already clears its input on close.

## Acceptance criteria

- [x] Reproduced first: a Playground check fails on main (the box reopens with "Bl").
- [x] Closing clears the box with the filter; reopened, the box is empty and every option is listed.

## Human test plan

- [x] Symbio, System → Administration → Platform, turn on the modules override, open the picker, type a few letters,
      click outside, open it again. Expected: the search box is empty and the full list shows. **PASS (the user, 2026-11-04).**

## Built (2026-11-04)

`_closeDropdown` clears `.dd-search` with `_filter`. Playground `multi-select-create-smoke` case 5 (type, outside
mousedown, reopen): 16/17 on main (the box kept "Bl"), 17/17 with the fix.
Full `verify.mjs`: 0 failing checks.

## Merged (2026-11-04)

Birko.Web main, tagged **v0.4.6** locally; Playground main. Not pushed.
