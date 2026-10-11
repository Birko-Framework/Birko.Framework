---
id: TASK-554
parent: EPIC-001
feature: null
status: verify
priority: P1
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [FIELD-024]
pr: null
github-issue: null
jira-key: null
---

# A validator's refusal never reaches the screen — problem+json was read as text, and its entries printed as [object Object]

## Context

Found 2026-10-11 in the human click-through of Symbio TASK-903 (tenant settings, order-tracking URL). Save was refused
(400, nothing stored) and **nothing appeared on screen**: no field error, no toast. Two Birko.Web defects, each enough
on its own, and both on every form that reads a validator's refusal, not only that page:

1. **`ApiClient` parsed only `application/json`.** ASP.NET sends a validation refusal as `application/problem+json`, so
   `resp.data` was the raw JSON **string**. `showFormError` found no `errors` map, and `apiErrorMessage` returned the
   whole string as "the message". Measured: the b-form error map held the full problem JSON on the fallback field.
2. **`showFormError` stringified each entry.** Symbio's validator sends `{ errorCode, message }` objects, so a parsed
   body would have printed `[object Object]` under the field.

Also: `apiErrorMessage` returned the generic ProblemDetails `title` ("Validation failed") ahead of the field's own
sentence, although the Core README already promised "the first validation message".

Symbio's `form-level-error.spec.ts` did not catch either one. Its refusal is a service `Result` (`{ error: { message } }`
as plain `application/json`); this is the validator half.

## Acceptance criteria

- [x] `ApiClient` parses any JSON media type (`/[/+]json\b/i`: `application/json`, `application/problem+json`,
      `…+json`).
- [x] `fieldErrorText(entry)` (core/http, exported) reads a string entry or `{ message }`. `showFormError` uses it.
- [x] `apiErrorMessage`: `detail`, then the first field message, then `title`, then a string.
- [x] READMEs of Core (`apiErrorMessage`) and Components (`showFormError`) say so.
- [x] Guard in the consumer: Symbio `tests/ui-e2e/tenant-settings-tracking-url.spec.ts`. It asserts the Slovak sentence
      is on the field and visible, is not the raw JSON and not `[object Object]`, and that nothing is stored.
      Mutation-checked: reverting either fix alone turns it red. `form-level-error.spec.ts` stays green.
- [x] Symbio's gate 3 green with the change (every page's error handling goes through it): 475 tests, 0 failed, 0 flaky (2026-10-11).
- [ ] Tag on the next push (README § Versions: a fix → PATCH).

## Out of scope

- Unit tests inside Birko.Web. The repo has none for `core/http`; the consumer specs are the guard today.
