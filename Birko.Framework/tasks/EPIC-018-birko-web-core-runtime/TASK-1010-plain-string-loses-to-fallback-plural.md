---
id: TASK-1010
parent: EPIC-018
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
related: [TASK-1009]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A locale's plain-string translation loses to a fallback locale's plural forms

## Context

Measured by Symbio on 2026-11-27 while mutation-testing its TASK-1140 on Birko.Web v0.5.9 ([[TASK-1009]]). Confirmed by
reading `Birko.Web.Core/src/i18n/i18n.ts`, `I18n.t()`. When `params` has `count`, `t()` first walks the whole chain
(the locale, then its fallbacks) looking for a **plural object**, and only then walks it again for a **string**.

So a locale whose key is a plain string is skipped in favour of the first fallback that holds plural forms. With
app locale cs, `cs: "položek"` (a string) and a fallback `sk: { one, few, many, other }`, the count 5 renders Slovak
"položiek", and 1–4 render Slovak's forms. The current locale's own translation should win, in whatever shape it is.

TASK-1009's CHANGELOG entry (v0.5.9) says "A plain string still works". That is true only when no fallback locale has
plural forms for the key.

## Acceptance criteria

- [x] Red first, a Core test (or a Playground i18n smoke): locale cs with a string, fallback sk with plural forms, `count`
  5. Today it returns the sk form.
- [x] `t()` resolves per locale in chain order. For each locale, a plural object is used with the count's category (else
  `other`), and a string is used as is. The first locale that has the key in either shape wins.
- [x] A plural object missing the count's category and `other` falls through to the next locale, as today.
- [x] Existing i18n smokes pass (`i18n-chain-smoke`, `locale-switch-check`, `i18n-gaps-check`).
- [x] CHANGELOG: correct the v0.5.9 claim in the new version's entry.

## Evidence 2026-11-27

Red first: the two plain-string cases in the Playground's `i18n-chain-smoke` failed on main, rendering "5 položiek" and
"1 položka" (22/24). Built on `task/TASK-1010` (v0.5.10): `i18n-chain-smoke` 24/24, `verify.mjs` 0 failing, `locale-switch-check`
47/47, `i18n-gaps-check` 10/10, `locale-keys-check` 23/23, Core `tsc --noEmit` clean.

## Out of scope

- Consumer bundles. Symbio already moved to plural objects.

## Human test plan

N/A — fully covered by automated tests.
