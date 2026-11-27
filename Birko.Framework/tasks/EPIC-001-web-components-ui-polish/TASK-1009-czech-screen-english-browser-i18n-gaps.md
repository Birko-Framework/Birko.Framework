---
id: TASK-1009
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-765, TASK-945]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A Czech screen in an English browser still shows English: b-chat time and texts, b-pagination's count word, b-select's "No matches"

## Context

Requested by the Symbio session on 2026-11-27 (its TASK-1140). It was verifying the FlowerFur owner's Czech admin: Chrome
in en-US, app locale cs. Symbio fixed its own side; these three gaps need the library.

1. **`b-chat`:** the message time was formatted with the browser's locale
   (`toLocaleTimeString([], …)`), so the Czech screen showed "02:19 PM". `Sending...` was hard-coded, and the
   placeholder and send-label defaults were English literals.
2. **`b-pagination`:** one word for any count (`bwc.pagination.items`), so Czech read "1 položek" (should be 1 položka,
   2–4 položky, 5+ položek; Slovak is the same) and English "1 items". The core i18n already resolves plural forms
   when `t()` gets a `count` and the key holds `{ one, few, many, other }`; `b-pagination` never passed the count.
   On a CRUD page, `base-crud-page` passed `label-items` as a fixed string from `bws.pagination.items`. An attribute
   wins over the bundle, so that path could not pluralize either.
3. **`b-select`:** "No matches" was read only from the `label-no-matches` attribute, which a `b-form` select field cannot
   set. `b-multi-select` already resolved `bwc.multiSelect.noMatches`.

The user approved taking these on (2026-11-27, "yes").

## Acceptance criteria

- [x] `b-chat` formats times through `getFormatter().time` (the app's locale). Its placeholder, send, empty, typing,
  sending and failed texts resolve `bwc.chat.*`; config and attribute still win.
- [x] `b-pagination` resolves `bwc.pagination.items` with `{ count }`; the English fallback and en.json read "1 item",
  and a `label-items` attribute still wins.
- [x] `PaginationLabels.items` may be `(count) => string`. `b-data-table` calls it with its total, and `base-crud-page`
  passes one over `bws.pagination.items`.
- [x] `b-select` resolves `bwc.select.noMatches`.
- [x] Keys added to `locales/en.json`. `locale-keys-check` passes.
- [x] Red first: the Playground's `i18n-gaps-check.mjs` fails 6 of 10 on Birko.Web main, with "03:19 PM",
  "1 items" and "No matches" under cs. It passes 10 of 10 on the branch.

## Evidence 2026-11-27

All checks ran on the branch (`BIRKO_SRC` set to the worktree):

| Check | Result |
|---|---|
| `i18n-gaps-check` | 10/10 (Chrome launched with `--lang=en-US`, app switched to cs) |
| `verify.mjs` | 0 failing |
| `locale-switch-check` | 47/47 |
| `keyboard-check` | 249/249 |
| `entry-points-check` | 433/433 |
| `locale-keys-check` | 23/23 |
| `rerender-leak-check` | pass |
| `tsc --noEmit` on Components and Shell | clean |

## Out of scope

- Symbio's own bundles. Symbio gets the plural forms by making `bws.pagination.items` (and `bwc.pagination.items`, if it
  carries it) an object of plural categories.
- A shared plural helper for Symbio's toasts (its TASK-1138). `t(key, { count })` already is one.

## Human test plan

1. In Symbio (or the Playground), set the app locale to cs in a browser whose language is English.
2. Open a chat with a message, and check its time reads 24-hour Czech (`15:19`), not `03:19 PM`.
3. Open a CRUD list with 1, 3 and 7 rows, after Symbio's `bws.pagination.items` holds plural forms. Check it reads
   1 položka, 3 položky, 7 položek.
4. In a form, type into a searchable select until nothing matches. Check it reads Symbio's `bwc.select.noMatches`.
