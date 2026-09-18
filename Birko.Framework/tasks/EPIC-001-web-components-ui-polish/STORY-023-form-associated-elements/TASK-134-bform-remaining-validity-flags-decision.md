---
id: TASK-134
parent: STORY-023
feature: FEATURE-001
status: todo
priority: P2
assignee: ai
created: 2026-08-01
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Decide whether `b-form.validate()` adopts the remaining validity flags, starting with `typeMismatch`

**This is a sweep and a decision, not an adoption.** It produces evidence and a recommendation per flag;
the behaviour change (if any) is a separate, decided step. `docs/features/FEATURE-001` row **D8** carries
the decision and is `proposed` — it needs `/feature decide` before any flag is turned on.

## Context

`Birko.Web.Components` `9402219` made `validate()` surface a control's own validity, deliberately on a
**fixed whitelist** rather than a `checkValidity()` gate: `badInput` always, plus
`rangeUnderflow` / `rangeOverflow` / `stepMismatch` only for the decimal-mode field types, where those
flags are `b-input`'s own and carry none of `type="number"`'s implicit `step=1`.

The whitelist exists because of one measured trap, which any future widening has to clear as well:
**`12.5` typed into a plain `type="number"` field is already natively invalid** — `step` defaults to 1, so
the browser reports `stepMismatch` ("the two nearest valid values are 12 and 13") — and `b-form` has
always ignored it. A blanket gate would newly reject fractional input in every consumer `number` field
that has ever worked (Symbio alone: `scrapPercent`, `moisturePercent`, `latePenaltyPercent`,
`organicMatterPercent`). Each remaining flag is that same trap in miniature, and each needs its own
survey rather than a blanket answer.

The excluded flags, with what is already known:

| Flag | Reaches `b-form` via | Already known |
|---|---|---|
| `typeMismatch` | `type: 'email'` (and `url` / `tel` if added) forwards `type` to the inner input | The most likely "yes". Pinned as excluded by a smoke check: `foo@` in a `type: 'email'` field is natively invalid and `validate()` passes it. `b-form` has an `email` **rule** whose regex (`/^[^\s@]+@[^\s@]+\.[^\s@]+$/`) and the browser's own grammar **disagree** — the survey must say which wins where both are present, and what happens to a field with `type: 'email'` and no rule |
| `patternMismatch` | not reachable today — `_fieldAttrs` never forwards a `pattern` attribute; the schema's `pattern` is a rule | Adoption is a no-op until `pattern` is forwarded. Decide whether it should be |
| `tooLong` / `tooShort` | not reachable today — `maxlength` / `minlength` are not forwarded either; `minLength` / `maxLength` are rules | Same shape as `pattern`. Note `tooShort` does not fire until the user interacts, so it and the rule would disagree on a programmatically-set value |
| `stepMismatch` / range on `type: 'number'` | implicit `step=1`, plus any `min`/`max` a consumer sets on the host | The trap itself. A "yes" here almost certainly requires `b-form` to emit an explicit `step` (`any`?) for `number` fields, which is itself a behaviour change |
| `valueMissing` | `required` forwarded to inner controls | Unreachable for most types (`required` returns before the control is consulted) and the one place it bites is a **different defect** — [[TASK-132]] |
| `customError` | the `error` attribute | **Stays excluded, permanently.** It is `b-form`'s own verdict from the previous run; adopting it would make a corrected field stay broken. Record as decided-no, do not re-survey |

## What this task delivers

1. A per-flag survey across the 16 local consumers: how many fields could newly fail, in which files, and
   for each one whether that new failure is a **bug being caught** or a **working form being broken**. The
   `number`/`step` precedent shows this cannot be answered from first principles — the four Symbio
   `*Percent` fields were the whole reason for the whitelist.
2. A recommendation per flag: adopt / adopt-with-a-forwarded-attribute / leave excluded — with the
   rule-vs-flag precedence spelled out wherever both mechanisms can fire on one field, matching the
   precedence the whitelist already set (rules first, control second, one message per field).
3. `docs/features/FEATURE-001/decisions.md` row **D8** moved out of `proposed` by `/feature decide`,
   recording the per-flag outcome.
4. The code and docs updated to match the decision, either way: `_controlVerdict`'s per-flag reasoning
   (`src/inputs/b-form.ts`), the flag table in `API.md` § *What `validate()` takes from the controls
   themselves*, and — for any flag that stays excluded — a smoke check that fails if it is later adopted
   silently, in the shape the `typeMismatch` and `valueMissing` exclusions already have.

## Acceptance criteria

- [ ] Every flag in the table above has a written verdict with its consumer-impact count, not a judgement
      call — including the two that are currently *unreachable*, whose verdict is about whether to forward
      the attribute at all.
- [ ] `typeMismatch` specifically: the interaction between the browser's email grammar and `b-form`'s
      `email` rule regex is documented, with the precedence decided for (a) rule only, (b) `type` only,
      (c) both.
- [ ] D8 is decided in the ledger, with the rationale, before any flag is switched on.
- [ ] Any flag that stays excluded is guarded by a check that fails on adoption — so the next reader meets
      a decision, not an unexplored idea.
- [ ] Any flag that is adopted lands with its own falsified checks and a `Recent Updates` note calling out
      the behaviour change, per STORY-052's rule that a default only changes when that *is* the ticket.
- [ ] Playground verifier green either way.

## Out of scope

- Fixing `required` on toggles ([[TASK-132]]) or radio collection ([[TASK-133]]). Both are defects with
  their own causes; this task decides only what `validate()` reads off a control's `validity`.
- Changing the rule engine's own messages or adding new `RuleType`s.
- `customError` — decided-no above, permanently, with its reason.

## Human test plan

N/A — covered by automated tests. The deliverable is a survey plus a ledger decision; any resulting
behaviour change is asserted headlessly.

## Cross-links

- Origin: `Birko.Web.Components` `9402219` (the whitelist and its per-flag reasoning),
  `Birko.Web.Playground` `16a72d8` (the exclusion checks, and the falsification runs that showed adopting
  `typeMismatch` breaks exactly one of them)
- `Birko.Web.Components/API.md` § *What `validate()` takes from the controls themselves* — the shipped
  flag table this task revises
- Ledger: `docs/features/FEATURE-001-web-components-ui-polish/decisions.md` row **D8** (`proposed`)
